using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TremorScope.App.Services;
using TremorScope.Core.Export;
using TremorScope.Core.Sensors;
using TremorScope.Infrastructure.Cloud;
using TremorScope.Infrastructure.Pacs;
using TremorScope.Infrastructure.Sensors;
using TremorScope.Infrastructure.Settings;

namespace TremorScope.App.ViewModels;

public sealed record Choice<T>(T Value, string Label);

/// <summary>
/// 設定画面。秘密（接続文字列・鍵）は入力欄に表示せず、
/// 入力されたときだけ置き換えて暗号化して保存する。
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly MainViewModel main;

    public SettingsViewModel(MainViewModel main)
    {
        this.main = main;
        var s = main.Services.Settings;
        FacilityName = s.FacilityName;
        SensorMode = s.SensorMode;
        SimulatorProfile = s.SimulatorProfile;
        IotDeviceId = s.IotDeviceId;
        IotConsumerGroup = s.IotConsumerGroup;
        MeasurementSeconds = s.MeasurementSeconds;
        TremorThresholdMg = s.TremorThresholdMg;
        CloudEnabled = s.CloudEnabled;
        CosmosEndpoint = s.CosmosEndpoint;
        CosmosDatabase = s.CosmosDatabase;
        CosmosContainer = s.CosmosContainer;
        PacsEnabled = s.PacsEnabled;
        PacsHost = s.PacsHost;
        PacsPort = s.PacsPort;
        PacsCallingAeTitle = s.PacsCallingAeTitle;
        PacsCalledAeTitle = s.PacsCalledAeTitle;
        PacsIdMode = s.PacsIdMode;

        var secrets = main.Services.SettingsStore.LoadSecrets();
        HasIotSecret = !string.IsNullOrWhiteSpace(secrets.IotHubConnectionString);
        HasCosmosKey = !string.IsNullOrWhiteSpace(secrets.CosmosKey);
        if (HasIotSecret) IotCheck = Describe(ConnectionStringCheck.Inspect(secrets.IotHubConnectionString));
    }

    public IReadOnlyList<Choice<SensorMode>> SensorModes { get; } =
    [
        new(SensorMode.Simulator, "模擬センサー（デモ・練習用）"),
        new(SensorMode.IotHub, "自作センサー（Azure IoT Hub 経由）"),
    ];

    public IReadOnlyList<Choice<SimulationProfile>> Profiles { get; } =
    [
        new(SimulationProfile.RestTremor, "安静時に目立つふるえ"),
        new(SimulationProfile.PosturalTremor, "姿勢時に目立つふるえ"),
        new(SimulationProfile.NoTremor, "目立ったふるえなし"),
    ];

    public IReadOnlyList<Choice<PacsPatientIdMode>> PacsIdModes { get; } =
    [
        new(PacsPatientIdMode.Pseudonym, "仮名 ID（おすすめ）"),
        new(PacsPatientIdMode.LocalId, "カルテ番号（院内の PACS で患者に紐づける場合）"),
    ];

    public IReadOnlyList<int> DurationChoices { get; } = [15, 20, 30, 45, 60];

    public string DataDirectory => AppServices.DataDirectory;

    [ObservableProperty] public partial string FacilityName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIotHub))]
    public partial SensorMode SensorMode { get; set; }

    public bool IsIotHub => SensorMode == SensorMode.IotHub;

    [ObservableProperty] public partial SimulationProfile SimulatorProfile { get; set; }

    [ObservableProperty] public partial string IotDeviceId { get; set; }

    [ObservableProperty] public partial string IotConsumerGroup { get; set; }

    [ObservableProperty] public partial int MeasurementSeconds { get; set; }

    [ObservableProperty] public partial double TremorThresholdMg { get; set; }

    [ObservableProperty] public partial bool CloudEnabled { get; set; }

    [ObservableProperty] public partial string CosmosEndpoint { get; set; }

    [ObservableProperty] public partial string CosmosDatabase { get; set; }

    [ObservableProperty] public partial string CosmosContainer { get; set; }

    [ObservableProperty] public partial bool PacsEnabled { get; set; }

    [ObservableProperty] public partial string PacsHost { get; set; }

    [ObservableProperty] public partial int PacsPort { get; set; }

    [ObservableProperty] public partial string PacsCallingAeTitle { get; set; }

    [ObservableProperty] public partial string PacsCalledAeTitle { get; set; }

    [ObservableProperty] public partial PacsPatientIdMode PacsIdMode { get; set; }

    // 秘密（入力されたときだけ使う）
    public string NewIotConnectionString { get; set; } = "";

    public string NewCosmosKey { get; set; } = "";

    [ObservableProperty] public partial bool HasIotSecret { get; set; }

    [ObservableProperty] public partial bool HasCosmosKey { get; set; }

    [ObservableProperty] public partial string? IotCheck { get; set; }

    [ObservableProperty] public partial string? CloudCheck { get; set; }

    [ObservableProperty] public partial string? PacsCheck { get; set; }

    [ObservableProperty] public partial string? Message { get; set; }

    [ObservableProperty] public partial bool IsBusy { get; set; }

    public CloudSyncService Cloud => main.Cloud;

    private AppSettings Collect() => new AppSettings
    {
        FacilityName = FacilityName.Trim(),
        SensorMode = SensorMode,
        SimulatorProfile = SimulatorProfile,
        IotDeviceId = IotDeviceId,
        IotConsumerGroup = IotConsumerGroup,
        MeasurementSeconds = MeasurementSeconds,
        TremorThresholdMg = TremorThresholdMg,
        CloudEnabled = CloudEnabled,
        CosmosEndpoint = CosmosEndpoint,
        CosmosDatabase = string.IsNullOrWhiteSpace(CosmosDatabase) ? "tremorscope" : CosmosDatabase.Trim(),
        CosmosContainer = string.IsNullOrWhiteSpace(CosmosContainer) ? "measurements" : CosmosContainer.Trim(),
        PacsEnabled = PacsEnabled,
        PacsHost = PacsHost,
        PacsPort = PacsPort,
        PacsCallingAeTitle = PacsCallingAeTitle,
        PacsCalledAeTitle = PacsCalledAeTitle,
        PacsIdMode = PacsIdMode,
    }.Normalized();

    private AppSecrets CollectSecrets()
    {
        var current = main.Services.SettingsStore.LoadSecrets();
        return current with
        {
            IotHubConnectionString = string.IsNullOrWhiteSpace(NewIotConnectionString) ? current.IotHubConnectionString : NewIotConnectionString.Trim(),
            CosmosKey = string.IsNullOrWhiteSpace(NewCosmosKey) ? current.CosmosKey : NewCosmosKey.Trim(),
        };
    }

    private static string Describe(ConnectionStringCheck check) =>
        !check.IsValid ? "✕ " + check.Problem
        : check.Warning is not null ? "△ 形式は正しいです。" + check.Warning
        : $"✓ 形式は正しいです（権限: {check.PolicyName}）。";

    [RelayCommand]
    private void Save()
    {
        if (!string.IsNullOrWhiteSpace(NewIotConnectionString))
        {
            var check = ConnectionStringCheck.Inspect(NewIotConnectionString);
            IotCheck = Describe(check);
            if (!check.IsValid)
            {
                Message = "IoT Hub の接続文字列を確認してください。保存していません。";
                return;
            }
        }
        var secrets = CollectSecrets();
        main.Services.SaveSettings(Collect(), secrets);
        HasIotSecret = !string.IsNullOrWhiteSpace(secrets.IotHubConnectionString);
        HasCosmosKey = !string.IsNullOrWhiteSpace(secrets.CosmosKey);
        Message = "保存しました。秘密の情報は、この PC のこのユーザーだけが読める形で暗号化しています。";
        SecretsSaved?.Invoke(this, EventArgs.Empty);
        _ = main.Cloud.SyncPendingAsync();
    }

    /// <summary>保存後に入力欄（パスワード欄）を空にするため</summary>
    public event EventHandler? SecretsSaved;

    [RelayCommand]
    private void CheckIot()
    {
        var value = string.IsNullOrWhiteSpace(NewIotConnectionString) ? main.Services.SettingsStore.LoadSecrets().IotHubConnectionString : NewIotConnectionString;
        IotCheck = Describe(ConnectionStringCheck.Inspect(value));
    }

    [RelayCommand]
    private async Task TestCloudAsync()
    {
        var key = CollectSecrets().CosmosKey;
        if (string.IsNullOrWhiteSpace(CosmosEndpoint) || string.IsNullOrWhiteSpace(key))
        {
            CloudCheck = "✕ エンドポイントとキーを入力してください。";
            return;
        }
        IsBusy = true;
        CloudCheck = "接続を確認しています…";
        try
        {
            var s = Collect();
            using var cloud = new CloudSync(new CloudSettings(s.CosmosEndpoint, key, s.CosmosDatabase, s.CosmosContainer));
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await cloud.TestAsync(cts.Token);
            CloudCheck = $"✓ 接続できました（データベース {s.CosmosDatabase} / コンテナー {s.CosmosContainer}）。";
        }
        catch (Exception ex)
        {
            CloudCheck = "✕ 接続できませんでした: " + FirstLine(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task TestPacsAsync()
    {
        var s = Collect();
        if (string.IsNullOrWhiteSpace(s.PacsHost) || string.IsNullOrWhiteSpace(s.PacsCalledAeTitle))
        {
            PacsCheck = "✕ ホストと送信先の AE タイトルを入力してください。";
            return;
        }
        IsBusy = true;
        PacsCheck = "C-ECHO で確認しています…";
        try
        {
            var client = new PacsClient(new PacsSettings(s.PacsHost, s.PacsPort, s.PacsCallingAeTitle, s.PacsCalledAeTitle));
            PacsCheck = await client.EchoAsync() ? "✓ PACS から応答がありました（C-ECHO 成功）。" : "✕ PACS から正しい応答がありませんでした。";
        }
        catch (Exception ex)
        {
            PacsCheck = "✕ 接続できませんでした: " + FirstLine(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task SyncNowAsync() => main.Cloud.SyncPendingAsync();

    [RelayCommand]
    private void OpenDataFolder() =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{AppServices.DataDirectory}\"") { UseShellExecute = true });

    private static string FirstLine(string s) => s.Split('\n', 2)[0].Trim();
}
