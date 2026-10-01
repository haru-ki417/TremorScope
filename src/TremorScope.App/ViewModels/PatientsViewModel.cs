using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TremorScope.Infrastructure.Storage;

namespace TremorScope.App.ViewModels;

public sealed class PatientRow(PatientListItem item)
{
    public PatientListItem Item { get; } = item;

    public string LocalId => Item.LocalId;

    public string PseudonymId => Item.PseudonymId;

    public string? Note => Item.Note;

    public string LastMeasured => Item.LastMeasuredAtUtc is DateTime t ? Labels.LocalTime(t) : "未測定";

    public string Count => $"{Item.SessionCount} 回";
}

/// <summary>患者を探す・登録する・選ぶ</summary>
public sealed partial class PatientsViewModel(MainViewModel main) : ObservableObject
{
    private CancellationTokenSource? searchCts;

    public ObservableCollection<PatientRow> Patients { get; } = [];

    [ObservableProperty]
    public partial string Search { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RegisterCommand))]
    public partial string NewLocalId { get; set; } = "";

    [ObservableProperty]
    public partial string NewNote { get; set; } = "";

    [ObservableProperty]
    public partial string? Message { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(MeasureCommand), nameof(HistoryCommand), nameof(DeleteCommand))]
    public partial PatientRow? Selected { get; set; }

    public bool HasSelection => Selected is not null;

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    partial void OnSearchChanged(string value) => _ = DebouncedLoadAsync();

    partial void OnSelectedChanged(PatientRow? value)
    {
        if (value is not null) main.SelectPatient(value.Item);
    }

    private async Task DebouncedLoadAsync()
    {
        searchCts?.Cancel();
        searchCts = new CancellationTokenSource();
        var token = searchCts.Token;
        try
        {
            await Task.Delay(200, token);
            await LoadAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task LoadAsync()
    {
        var list = await main.Services.Store.ListPatientsAsync(Search);
        Patients.Clear();
        foreach (var p in list) Patients.Add(new PatientRow(p));
        IsEmpty = Patients.Count == 0;
        if (main.CurrentPatient is { } current)
            Selected = Patients.FirstOrDefault(p => p.Item.Id == current.Id);
    }

    private bool CanRegister => !string.IsNullOrWhiteSpace(NewLocalId);

    [RelayCommand(CanExecute = nameof(CanRegister))]
    private async Task RegisterAsync()
    {
        try
        {
            var patient = await main.Services.Store.GetOrCreatePatientAsync(NewLocalId, NewNote);
            Message = patient.SessionCount > 0 ? $"登録済みの患者（{patient.LocalId}）を選びました。" : $"{patient.LocalId} を登録しました。";
            NewLocalId = "";
            NewNote = "";
            Search = "";
            main.SelectPatient(patient);
            await LoadAsync();
        }
        catch (ArgumentException ex)
        {
            Message = ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Measure() => main.ShowMeasure();

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void History() => main.ShowHistory();

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteAsync()
    {
        if (Selected is null) return;
        var answer = MessageBox.Show(
            $"カルテ番号 {Selected.LocalId} の患者と、{Selected.Item.SessionCount} 回分の測定をすべて削除します。\n元に戻すことはできません。よろしいですか？",
            "患者の削除", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        string? pseudonym = await main.Services.Store.DeletePatientAsync(Selected.Item.Id);
        string cloud = pseudonym is null ? "" : await main.Cloud.DeletePatientAsync(pseudonym) ?? "";
        Message = $"削除しました。{cloud}";
        main.SelectPatient(null);
        Selected = null;
        await LoadAsync();
    }
}
