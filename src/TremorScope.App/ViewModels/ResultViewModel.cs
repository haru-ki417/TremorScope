using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TremorScope.App.Services;
using TremorScope.Core.Analysis;
using TremorScope.Core.Domain;
using TremorScope.Core.Export;
using Condition = TremorScope.Core.Domain.Condition;

namespace TremorScope.App.ViewModels;

/// <summary>1 回の測定の結果（安静時と姿勢時の比較）</summary>
public sealed partial class ResultViewModel(MainViewModel main, Guid sessionId) : ObservableObject
{
    private IReadOnlyList<Recording> recordings = [];

    public event EventHandler? SpectraReady;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoaded))]
    public partial SessionSummary? Session { get; set; }

    public bool IsLoaded => Session is not null;

    [ObservableProperty]
    public partial ConditionCard? Rest { get; set; }

    [ObservableProperty]
    public partial ConditionCard? Postural { get; set; }

    public AnalysisResult? RestAnalysis { get; private set; }

    public AnalysisResult? PosturalAnalysis { get; private set; }

    [ObservableProperty]
    public partial string PatternTitle { get; set; } = "";

    [ObservableProperty]
    public partial string PatternSummary { get; set; } = "";

    [ObservableProperty]
    public partial TremorPattern Pattern { get; set; }

    [ObservableProperty]
    public partial double RestShare { get; set; } = 0.5;

    [ObservableProperty]
    public partial string Header { get; set; } = "";

    [ObservableProperty]
    public partial string DeliveryStatus { get; set; } = "";

    [ObservableProperty]
    public partial string? Message { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendToPacsCommand))]
    public partial bool IsBusy { get; set; }

    public string Disclaimer => Labels.Disclaimer;

    public bool PacsEnabled => main.Services.Settings.PacsEnabled;

    public async Task LoadAsync()
    {
        var loaded = await main.Services.Store.GetSessionAsync(sessionId);
        if (loaded is null)
        {
            Message = "測定が見つかりません（削除された可能性があります）。";
            return;
        }
        var (summary, waves) = loaded.Value;
        recordings = waves;
        // スペクトルは保存した波形から計算し直す（同じ計算なので結果は保存時と同じ）
        foreach (var r in waves)
        {
            var a = TremorAnalyzer.Analyze(r);
            if (r.Condition == Condition.Rest) RestAnalysis = a;
            else PosturalAnalysis = a;
        }
        Rest = summary.Rest is null ? null : new ConditionCard(summary.Rest);
        Postural = summary.Postural is null ? null : new ConditionCard(summary.Postural);
        Pattern = summary.Comparison.Pattern;
        PatternTitle = Labels.Pattern(summary.Comparison.Pattern);
        PatternSummary = summary.Comparison.Summary;
        double r1 = summary.Rest?.RmsAccelerationMg ?? 0, p1 = summary.Postural?.RmsAccelerationMg ?? 0;
        RestShare = r1 + p1 > 0 ? r1 / (r1 + p1) : 0.5;
        Header = $"{Labels.LocalTime(summary.MeasuredAtUtc)} · {Labels.Hand(summary.Hand)}{(string.IsNullOrWhiteSpace(summary.Note) ? "" : " · " + summary.Note)}";
        Session = summary;
        await RefreshDeliveryAsync();
        SpectraReady?.Invoke(this, EventArgs.Empty);
    }

    private async Task RefreshDeliveryAsync()
    {
        var (cloud, pacs) = await main.Services.Store.GetDeliveryStatusAsync(sessionId);
        string c = !main.Services.Settings.CloudEnabled ? "クラウド保存: オフ" : cloud is DateTime t ? $"クラウド保存済み（{Labels.LocalTime(t)}）" : "クラウド: 未送信（自動で送ります）";
        string p = !main.Services.Settings.PacsEnabled ? "" : pacs is DateTime t2 ? $" · PACS 送信済み（{Labels.LocalTime(t2)}）" : " · PACS: 未送信";
        DeliveryStatus = c + p;
    }

    private ReportData BuildReport(PacsPatientIdMode idMode)
    {
        var s = Session!;
        return new ReportData(
            s,
            idMode == PacsPatientIdMode.LocalId ? "カルテ番号" : "仮名 ID",
            idMode == PacsPatientIdMode.LocalId ? s.LocalId : s.PseudonymId,
            main.Services.Settings.FacilityName,
            Rest, Postural,
            PlotTheme.SpectrumImage(RestAnalysis, PosturalAnalysis, 1180, 900),
            AppServices.AppVersion);
    }

    [RelayCommand]
    private void SaveImage()
    {
        if (Session is null) return;
        var dialog = new SaveFileDialog
        {
            Title = "レポートを画像で保存",
            Filter = "PNG 画像 (*.png)|*.png",
            FileName = $"tremor_{Session.LocalId}_{Session.MeasuredAtUtc.ToLocalTime():yyyyMMdd_HHmm}.png",
        };
        if (dialog.ShowDialog() != true) return;
        var bitmap = ReportRenderer.Render(BuildReport(PacsPatientIdMode.LocalId));
        ReportRenderer.SavePng(bitmap, dialog.FileName);
        Message = "レポートを保存しました。";
    }

    [RelayCommand]
    private void Print()
    {
        if (Session is null) return;
        if (ReportRenderer.Print(BuildReport(PacsPatientIdMode.LocalId), $"振戦測定 {Session.LocalId}"))
            Message = "印刷に送りました。";
    }

    [RelayCommand]
    private void ExportWaveform()
    {
        if (Session is null || recordings.Count == 0) return;
        var dialog = new OpenFolderDialog { Title = "波形の CSV を保存するフォルダー" };
        if (dialog.ShowDialog() != true) return;
        foreach (var r in recordings)
        {
            string name = $"tremor_{Session.PseudonymId}_{Session.MeasuredAtUtc.ToLocalTime():yyyyMMdd_HHmm}_{r.Condition.ToString().ToLowerInvariant()}.csv";
            File.WriteAllText(Path.Combine(dialog.FolderName, name), CsvExporter.Waveform(r), CsvExporter.Encoding);
        }
        Message = $"波形を {recordings.Count} 個の CSV に保存しました（ファイル名は仮名 ID）。";
    }

    private bool CanSendToPacs => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSendToPacs))]
    private async Task SendToPacsAsync()
    {
        if (Session is null) return;
        var pacs = main.Services.CreatePacs();
        if (pacs is null)
        {
            Message = "PACS の送信先が設定されていません（設定画面）。";
            return;
        }
        IsBusy = true;
        Message = "PACS に送信しています…";
        try
        {
            var mode = main.Services.Settings.PacsIdMode;
            var bitmap = ReportRenderer.Render(BuildReport(mode));
            var rgb = ReportRenderer.ToRgb(bitmap, out int w, out int h);
            var file = DicomReportBuilder.Build(rgb, w, h, Session, mode, AppServices.AppVersion);
            await pacs.StoreAsync(file);
            await main.Services.Store.MarkPacsSentAsync(sessionId);
            await RefreshDeliveryAsync();
            Message = "PACS に送信しました。";
        }
        catch (Exception ex)
        {
            Message = "PACS に送信できませんでした: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void MeasureAgain() => main.ShowMeasure();

    [RelayCommand]
    private void Back() => main.ShowHistory();

    public void Notify(string message) => Message = message;

    internal static void ShowError(string message) => MessageBox.Show(message, "TremorScope", MessageBoxButton.OK, MessageBoxImage.Warning);
}
