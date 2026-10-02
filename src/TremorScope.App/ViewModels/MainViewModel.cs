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
        var vm = new PatientsViewModel(this);
        Section = "patients";
        CurrentPage = vm;
        Load(vm.LoadAsync());
    }

    [RelayCommand(CanExecute = nameof(HasPatient))]
    public void ShowMeasure()
    {
        if (CurrentPatient is null) return;
        var vm = new MeasureViewModel(this, CurrentPatient);
        Section = "measure";
        CurrentPage = vm;
    }

    [RelayCommand(CanExecute = nameof(HasPatient))]
    public void ShowHistory()
    {
        if (CurrentPatient is null) return;
        var vm = new HistoryViewModel(this, CurrentPatient);
        Section = "history";
        CurrentPage = vm;
        Load(vm.LoadAsync());
    }

    [RelayCommand]
    public void ShowSettings()
    {
        var vm = new SettingsViewModel(this);
        Section = "settings";
        CurrentPage = vm;
    }

    public void ShowResult(Guid sessionId)
    {
        var vm = new ResultViewModel(this, sessionId);
        Section = "history";
        CurrentPage = vm;
        Load(vm.LoadAsync());
    }

    /// <summary>画面のデータの読み込みに失敗したら、理由を知らせる（何も出ないまま空の画面にしない）</summary>
    private static async void Load(Task loading)
    {
        try
        {
            await loading;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show("データを読み込めませんでした。\n\n" + ex.Message, "TremorScope",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
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
