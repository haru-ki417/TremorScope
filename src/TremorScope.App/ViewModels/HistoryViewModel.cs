using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TremorScope.Core.Domain;
using TremorScope.Core.Export;
using TremorScope.Infrastructure.Storage;

namespace TremorScope.App.ViewModels;

public sealed class SessionRow(SessionSummary s)
{
    public SessionSummary Summary => s;

    public string When => Labels.LocalTime(s.MeasuredAtUtc);

    public string Hand => Labels.Hand(s.Hand);

    public string Pattern => Labels.Pattern(s.Comparison.Pattern);

    public string RestRms => s.Rest is null ? "—" : $"{s.Rest.RmsAccelerationMg:0.0}";

    public string RestPeak => s.Rest is null ? "—" : $"{s.Rest.PeakFrequencyHz:0.0}";

    public string PosturalRms => s.Postural is null ? "—" : $"{s.Postural.RmsAccelerationMg:0.0}";

    public string PosturalPeak => s.Postural is null ? "—" : $"{s.Postural.PeakFrequencyHz:0.0}";

    public string? Note => s.Note;
}

/// <summary>1 人の患者の測定の履歴と、経過のグラフ</summary>
public sealed partial class HistoryViewModel(MainViewModel main, PatientListItem patient) : ObservableObject
{
    public PatientListItem Patient { get; } = patient;

    public ObservableCollection<SessionRow> Sessions { get; } = [];

    public event EventHandler? TrendReady;

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial bool IncludeLocalId { get; set; }

    [ObservableProperty]
    public partial string? Message { get; set; }

    [ObservableProperty]
    public partial SessionRow? Selected { get; set; }

    partial void OnSelectedChanged(SessionRow? value)
    {
        if (value is not null) main.ShowResult(value.Summary.Id);
    }

    public async Task LoadAsync()
    {
        var list = await main.Services.Store.ListSessionsAsync(Patient.Id);
        Sessions.Clear();
        foreach (var s in list) Sessions.Add(new SessionRow(s));
        IsEmpty = Sessions.Count == 0;
        TrendReady?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Measure() => main.ShowMeasure();

    [RelayCommand]
    private void ExportCsv()
    {
        if (Sessions.Count == 0) return;
        var dialog = new SaveFileDialog
        {
            Title = "測定の一覧を CSV で保存",
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"tremor_{(IncludeLocalId ? Patient.LocalId : Patient.PseudonymId)}_{DateTime.Now:yyyyMMdd}.csv",
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, CsvExporter.Sessions(Sessions.Select(s => s.Summary), IncludeLocalId), CsvExporter.Encoding);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Message = "保存できませんでした（ファイルを開いたままになっていませんか）: " + ex.Message;
            return;
        }
        Message = IncludeLocalId ? "保存しました（カルテ番号を含みます。取り扱いに注意してください）。" : "保存しました（カルテ番号は含みません）。";
    }
}
