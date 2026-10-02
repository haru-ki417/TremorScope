using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TremorScope.App.ViewModels;
using TremorScope.Core.Analysis;
using TremorScope.Core.Domain;
using TremorScope.Core.Export;
using TremorScope.Core.Sensors;
using Condition = TremorScope.Core.Domain.Condition;
using TremorScope.Infrastructure.Settings;
using TremorScope.Infrastructure.Storage;

namespace TremorScope.App.Services;

/// <summary>
/// 見本の画面を画像に保存する（README の画像づくりと、Windows 上での表示の確認のため）。
/// 一時フォルダーに見本のデータ（DEMO-xxx）を作るので、本番のデータには触れない。
///   TremorScope.exe --snapshots フォルダー
/// </summary>
public static class SnapshotRunner
{
    public static async Task RunAsync(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        string temp = Path.Combine(Path.GetTempPath(), "TremorScope-snapshots-" + Guid.NewGuid().ToString("N")[..8]);
        var log = new List<string>();
        try
        {
            var services = await AppServices.CreateAsync(temp);
            services.SaveSettings(new AppSettings { FacilityName = "サンプル脳神経内科クリニック", SimulatorProfile = SimulationProfile.RestTremor }, null);
            var demo = await SeedAsync(services);

            var main = new MainViewModel(services);
            var window = new MainWindow { DataContext = main, Width = 1360, Height = 860, WindowStartupLocation = WindowStartupLocation.CenterScreen };
            window.Show();

            main.SelectPatient(demo);
            main.ShowPatients();
            await SettleAsync(1500);
            Save(window, outputDirectory, "01-patients.png", log);

            main.ShowMeasure();
            await SettleAsync(3000);
            Save(window, outputDirectory, "02-measure-prepare.png", log);

            if (main.CurrentPage is MeasureViewModel measure)
            {
                measure.BeginCommand.Execute(null);
                await SettleAsync(11000);
                Save(window, outputDirectory, "03-measure-recording.png", log);
            }

            var sessions = await services.Store.ListSessionsAsync(demo.Id);
            main.ShowResult(sessions[0].Id);
            await SettleAsync(2500);
            Save(window, outputDirectory, "04-result.png", log);

            main.ShowHistory();
            await SettleAsync(2000);
            Save(window, outputDirectory, "05-history.png", log);

            main.ShowSettings();
            await SettleAsync(1500);
            Save(window, outputDirectory, "06-settings.png", log);

            // レポート（画像と DICOM）
            var loaded = (await services.Store.GetSessionAsync(sessions[0].Id))!.Value;
            var analyses = loaded.Recordings.ToDictionary(r => r.Condition, TremorAnalyzer.Analyze);
            var report = new ReportData(loaded.Summary, "カルテ番号", loaded.Summary.LocalId, services.Settings.FacilityName,
                loaded.Summary.Rest is null ? null : new ConditionCard(loaded.Summary.Rest),
                loaded.Summary.Postural is null ? null : new ConditionCard(loaded.Summary.Postural),
                PlotTheme.SpectrumImage(analyses.GetValueOrDefault(Condition.Rest), analyses.GetValueOrDefault(Condition.Postural), 1180, 900),
                AppServices.AppVersion);
            var bitmap = ReportRenderer.Render(report);
            ReportRenderer.SavePng(bitmap, Path.Combine(outputDirectory, "07-report.png"));
            // DICOM（仮名モード）は、画像の中の ID も仮名 ID にしたレポートから作る
            var pseudonymBitmap = ReportRenderer.Render(report with { PatientIdLabel = "仮名 ID", PatientIdValue = loaded.Summary.PseudonymId });
            var rgb = ReportRenderer.ToRgb(pseudonymBitmap, out int w, out int h);
            var dicom = DicomReportBuilder.Build(rgb, w, h, loaded.Summary, PacsPatientIdMode.Pseudonym, AppServices.AppVersion);
            await dicom.SaveAsync(Path.Combine(outputDirectory, "08-report-pseudonym.dcm"));
            log.Add("07-report.png / 08-report-pseudonym.dcm");

            main.ShowPatients();
            await SettleAsync(500);
            window.Close();
            log.Add("ok");
        }
        catch (Exception ex)
        {
            log.Add("ERROR: " + ex);
        }
        finally
        {
            try
            {
                await File.WriteAllLinesAsync(Path.Combine(outputDirectory, "snapshots.log"), log);
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                Directory.Delete(temp, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }
    }

    /// <summary>見本の患者と、数週間分の測定を作る</summary>
    private static async Task<PatientListItem> SeedAsync(AppServices services)
    {
        var store = services.Store;
        var now = DateTime.UtcNow;
        var p1 = await store.GetOrCreatePatientAsync("DEMO-001", "見本（安静時に目立つふるえ）");
        // 少しずつ小さくなっていく経過
        double[] gains = [1.25, 1.1, 1.0, 0.85];
        for (int i = 0; i < gains.Length; i++)
        {
            var recs = new[] { Condition.Rest, Condition.Postural }
                .Select(c => Scaled(SimulatedSource.CreateRecording(SimulationProfile.RestTremor, c, 20, seed: 10 + i * 2 + (int)c), gains[i]))
                .Select(r => new StoredRecording(r, TremorAnalyzer.Analyze(r).Metrics)).ToList();
            await store.SaveSessionAsync(p1.Id, Hand.Right, i == gains.Length - 1 ? "服薬 1 時間後" : null, recs, AppServices.AppVersion,
                now.AddDays(-7 * (gains.Length - 1 - i)).AddHours(-i));
        }
        foreach (var (id, profile, note) in new[] { ("DEMO-002", SimulationProfile.PosturalTremor, "見本（姿勢時に目立つふるえ）"), ("DEMO-003", SimulationProfile.NoTremor, "見本（目立ったふるえなし）") })
        {
            var p = await store.GetOrCreatePatientAsync(id, note);
            var recs = new[] { Condition.Rest, Condition.Postural }
                .Select(c => SimulatedSource.CreateRecording(profile, c, 20, seed: 40 + (int)c))
                .Select(r => new StoredRecording(r, TremorAnalyzer.Analyze(r).Metrics)).ToList();
            await store.SaveSessionAsync(p.Id, Hand.Left, null, recs, AppServices.AppVersion, now.AddDays(-3));
        }
        return (await store.ListPatientsAsync("DEMO-001"))[0];
    }

    /// <summary>重力（平均）はそのままに、ふるえの成分だけを大きさを変える</summary>
    private static Recording Scaled(Recording r, double gain) =>
        new(r.Condition, r.SampleRate, r.Axes.Select(a =>
        {
            double mean = a.Average();
            return a.Select(v => mean + (v - mean) * gain).ToArray();
        }).ToArray(), r.MissingSamples, r.StartedAtUtc, r.DeviceId);

    private static async Task SettleAsync(int milliseconds)
    {
        await Task.Delay(milliseconds);
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static void Save(Window window, string dir, string name, List<string> log)
    {
        if (window.Content is not FrameworkElement root) return;
        var dpi = VisualTreeHelper.GetDpi(window);
        int w = (int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX), h = (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY);
        var bitmap = new RenderTargetBitmap(w, h, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(root);
        ReportRenderer.SavePng(bitmap, Path.Combine(dir, name));
        log.Add($"{name} {w}x{h}");
    }
}
