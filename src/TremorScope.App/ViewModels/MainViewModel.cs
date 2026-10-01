using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TremorScope.App.Services;
using TremorScope.Infrastructure.Storage;

namespace TremorScope.App.ViewModels;

/// <summary>画面の切り替えと、選んでいる患者を管理する</summary>
public sealed partial class MainViewModel : ObservableObject
{
    public MainViewModel(AppServices services)
    {
        Services = services;
        Cloud = new CloudSyncService(services);
        services.SettingsChanged += (_, _) => OnPropertyChanged(nameof(SensorDescription));
        ShowPatients();
        _ = Cloud.SyncPendingAsync();
    }

    public AppServices Services { get; }

    public CloudSyncService Cloud { get; }

    public string AppVersion => AppServices.AppVersion;

    public string SensorDescription => Services.SensorDescription;

    public string FacilityName => string.IsNullOrWhiteSpace(Services.Settings.FacilityName) ? "施設名は設定画面で入力できます" : Services.Settings.FacilityName;

    [ObservableProperty]
    public partial object? CurrentPage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPatient))]
    [NotifyCanExecuteChangedFor(nameof(ShowMeasureCommand), nameof(ShowHistoryCommand))]
    public partial PatientListItem? CurrentPatient { get; set; }

    public bool HasPatient => CurrentPatient is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPatients), nameof(IsMeasure), nameof(IsHistory), nameof(IsSettings))]
    public partial string Section { get; set; } = "";

    public bool IsPatients => Section == "patients";

    public bool IsMeasure => Section == "measure";

    public bool IsHistory => Section == "history";

    public bool IsSettings => Section == "settings";

    partial void OnCurrentPageChanged(object? oldValue, object? newValue)
    {
        if (oldValue is IAsyncDisposable d) _ = d.DisposeAsync().AsTask();
    }

    [RelayCommand]
    public void ShowPatients()
    {
        Section = "patients";
        var vm = new PatientsViewModel(this);
        CurrentPage = vm;
        _ = vm.LoadAsync();
    }

    [RelayCommand(CanExecute = nameof(HasPatient))]
    public void ShowMeasure()
    {
        if (CurrentPatient is null) return;
        Section = "measure";
        CurrentPage = new MeasureViewModel(this, CurrentPatient);
    }

    [RelayCommand(CanExecute = nameof(HasPatient))]
    public void ShowHistory()
    {
        if (CurrentPatient is null) return;
        Section = "history";
        var vm = new HistoryViewModel(this, CurrentPatient);
        CurrentPage = vm;
        _ = vm.LoadAsync();
    }

    [RelayCommand]
    public void ShowSettings()
    {
        Section = "settings";
        CurrentPage = new SettingsViewModel(this);
    }

    public void ShowResult(Guid sessionId)
    {
        Section = "history";
        var vm = new ResultViewModel(this, sessionId);
        CurrentPage = vm;
        _ = vm.LoadAsync();
    }

    public void SelectPatient(PatientListItem? patient)
    {
        CurrentPatient = patient;
    }

    /// <summary>件数などを最新にする</summary>
    public async Task RefreshCurrentPatientAsync()
    {
        if (CurrentPatient is null) return;
        var list = await Services.Store.ListPatientsAsync(CurrentPatient.LocalId);
        CurrentPatient = list.FirstOrDefault(p => p.Id == CurrentPatient.Id);
    }
}
