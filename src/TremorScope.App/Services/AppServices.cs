using System.IO;
using System.Reflection;
using TremorScope.Core.Domain;
using TremorScope.Core.Privacy;
using TremorScope.Core.Sensors;
using TremorScope.Infrastructure.Cloud;
using TremorScope.Infrastructure.Pacs;
using TremorScope.Infrastructure.Sensors;
using TremorScope.Infrastructure.Settings;
using TremorScope.Infrastructure.Storage;

namespace TremorScope.App.Services;

/// <summary>アプリ全体で使う部品をまとめる</summary>
public sealed class AppServices
{
    private AppServices(SettingsStore settingsStore, AppSettings settings, MeasurementStore store)
    {
        SettingsStore = settingsStore;
        Settings = settings;
        Store = store;
    }

    /// <summary>データの置き場所（この PC の中だけ。ほかの PC へ同期されない場所）</summary>
    public static string DataDirectory { get; private set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TremorScope");

    public static string AppVersion { get; } =
        (typeof(AppServices).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0").Split('+')[0];

    public SettingsStore SettingsStore { get; }

    public AppSettings Settings { get; private set; }

    public MeasurementStore Store { get; }

    public event EventHandler? SettingsChanged;

    /// <param name="dataDirectory">データの置き場所を変える（見本の画面を作るときなど、本番のデータに触れないため）</param>
    public static async Task<AppServices> CreateAsync(string? dataDirectory = null)
    {
        if (dataDirectory is not null) DataDirectory = dataDirectory;
        var settingsStore = new SettingsStore(DataDirectory, new DpapiProtector());
        var settings = settingsStore.LoadSettings();
        var pseudonymizer = new Pseudonymizer(settingsStore.GetOrCreateSiteKey());
        string dbPath = settingsStore.DatabasePath;
        var store = new MeasurementStore(() => TremorDbContext.Open(dbPath), pseudonymizer);
        await store.InitializeAsync();
        return new AppServices(settingsStore, settings, store);
    }

    public void SaveSettings(AppSettings settings, AppSecrets? secrets)
    {
        SettingsStore.SaveSettings(settings);
        if (secrets is not null) SettingsStore.SaveSecrets(secrets);
        Settings = settings.Normalized();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>今の設定に合ったセンサーを用意する</summary>
    public ISampleSource CreateSource(Func<Condition> currentCondition)
    {
        if (Settings.SensorMode == SensorMode.IotHub)
        {
            var secrets = SettingsStore.LoadSecrets();
            var check = ConnectionStringCheck.Inspect(secrets.IotHubConnectionString);
            if (!check.IsValid) throw new InvalidOperationException("IoT Hub の接続文字列が設定されていません。設定画面で入力してください。");
            return new IotHubSource(secrets.IotHubConnectionString, Settings.IotConsumerGroup,
                string.IsNullOrWhiteSpace(Settings.IotDeviceId) ? null : Settings.IotDeviceId);
        }
        return new SimulatedSource(Settings.SimulatorProfile, currentCondition);
    }

    public string SensorDescription => Settings.SensorMode == SensorMode.IotHub
        ? $"IoT Hub{(string.IsNullOrWhiteSpace(Settings.IotDeviceId) ? "" : $" · {Settings.IotDeviceId}")}"
        : "模擬センサー（デモ）";

    /// <summary>クラウド保存がオンで、必要な設定がそろっていれば作る</summary>
    public CloudSync? CreateCloud()
    {
        if (!Settings.CloudEnabled || string.IsNullOrWhiteSpace(Settings.CosmosEndpoint)) return null;
        var key = SettingsStore.LoadSecrets().CosmosKey;
        if (string.IsNullOrWhiteSpace(key)) return null;
        return new CloudSync(new CloudSettings(Settings.CosmosEndpoint, key, Settings.CosmosDatabase, Settings.CosmosContainer));
    }

    public PacsClient? CreatePacs() =>
        Settings.PacsEnabled && !string.IsNullOrWhiteSpace(Settings.PacsHost) && !string.IsNullOrWhiteSpace(Settings.PacsCalledAeTitle)
            ? new PacsClient(new PacsSettings(Settings.PacsHost, Settings.PacsPort, Settings.PacsCallingAeTitle, Settings.PacsCalledAeTitle))
            : null;
}
