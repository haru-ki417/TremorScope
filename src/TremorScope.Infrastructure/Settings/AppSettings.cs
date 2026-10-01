using TremorScope.Core.Export;
using TremorScope.Core.Sensors;

namespace TremorScope.Infrastructure.Settings;

/// <summary>センサーのつなぎ方</summary>
public enum SensorMode
{
    /// <summary>練習用・デモ用の模擬センサー</summary>
    Simulator,
    /// <summary>自作センサー → Azure IoT Hub</summary>
    IotHub,
}

/// <summary>
/// 画面の設定のうち、秘密ではないもの（そのまま JSON に保存する）。
/// 接続文字列や鍵はここに入れず、<see cref="AppSecrets"/> に分けて暗号化する。
/// </summary>
public sealed record AppSettings
{
    public string FacilityName { get; init; } = "";
    public SensorMode SensorMode { get; init; } = SensorMode.Simulator;
    public SimulationProfile SimulatorProfile { get; init; } = SimulationProfile.RestTremor;
    public string IotDeviceId { get; init; } = "";
    public string IotConsumerGroup { get; init; } = "$Default";
    public int MeasurementSeconds { get; init; } = 20;
    public double TremorThresholdMg { get; init; } = 10;

    public bool CloudEnabled { get; init; }
    public string CosmosEndpoint { get; init; } = "";
    public string CosmosDatabase { get; init; } = "tremorscope";
    public string CosmosContainer { get; init; } = "measurements";

    public bool PacsEnabled { get; init; }
    public string PacsHost { get; init; } = "";
    public int PacsPort { get; init; } = 104;
    public string PacsCallingAeTitle { get; init; } = "TREMORSCOPE";
    public string PacsCalledAeTitle { get; init; } = "";
    public PacsPatientIdMode PacsIdMode { get; init; } = PacsPatientIdMode.Pseudonym;

    /// <summary>入力値を安全な範囲に収める（手で書き換えた JSON を読んだときのため）</summary>
    public AppSettings Normalized() => this with
    {
        MeasurementSeconds = Math.Clamp(MeasurementSeconds, 10, 60),
        TremorThresholdMg = double.IsFinite(TremorThresholdMg) ? Math.Clamp(TremorThresholdMg, 1, 200) : 10,
        PacsPort = PacsPort is > 0 and <= 65535 ? PacsPort : 104,
        IotConsumerGroup = string.IsNullOrWhiteSpace(IotConsumerGroup) ? "$Default" : IotConsumerGroup.Trim(),
        IotDeviceId = IotDeviceId.Trim(),
        CosmosEndpoint = CosmosEndpoint.Trim(),
        PacsHost = PacsHost.Trim(),
        PacsCallingAeTitle = PacsCallingAeTitle.Trim().ToUpperInvariant(),
        PacsCalledAeTitle = PacsCalledAeTitle.Trim().ToUpperInvariant(),
    };
}

/// <summary>秘密の設定（暗号化して保存する）</summary>
public sealed record AppSecrets
{
    public string IotHubConnectionString { get; init; } = "";
    public string CosmosKey { get; init; } = "";
    /// <summary>仮名 ID を作るための施設ごとの鍵（Base64）。なくすと同じ患者に同じ仮名 ID を付けられなくなる</summary>
    public string SiteKey { get; init; } = "";
}
