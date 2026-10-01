using System.Text;
using Newtonsoft.Json;
using TremorScope.Core.Analysis;
using TremorScope.Core.Domain;
using TremorScope.Core.Privacy;
using TremorScope.Core.Sensors;
using TremorScope.Infrastructure.Cloud;
using TremorScope.Infrastructure.Sensors;
using TremorScope.Infrastructure.Settings;
using TremorScope.Infrastructure.Storage;

namespace TremorScope.Tests;

public sealed class InfrastructureTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "tremorscope-tests-" + Guid.NewGuid().ToString("N"));
    private readonly Pseudonymizer pseudonymizer = new(Encoding.UTF8.GetBytes("test-site-key-0123456789abcdef"));

    public InfrastructureTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }

    private async Task<MeasurementStore> NewStoreAsync()
    {
        string path = Path.Combine(dir, "test.db");
        var store = new MeasurementStore(() => TremorDbContext.Open(path), pseudonymizer);
        await store.InitializeAsync(TestContext.Current.CancellationToken);
        return store;
    }

    private static List<StoredRecording> SimulatedSession() =>
    [
        Stored(SimulatedSource.CreateRecording(SimulationProfile.RestTremor, Condition.Rest, 20, seed: 3)),
        Stored(SimulatedSource.CreateRecording(SimulationProfile.RestTremor, Condition.Postural, 20, seed: 4)),
    ];

    private static StoredRecording Stored(Recording r) => new(r, TremorAnalyzer.Analyze(r).Metrics);

    [Fact]
    public void 波形は_float_に詰めて元に戻せる()
    {
        double[][] axes = [[0.1, -0.25, 1.5], [0, 2, -3], [0.98, 0.99, 1.01]];
        var back = MeasurementStore.Unpack(MeasurementStore.Pack(axes), 3, 3);
        for (int a = 0; a < 3; a++)
            for (int i = 0; i < 3; i++)
                Assert.Equal(axes[a][i], back[a][i], 1e-6);
    }

    [Fact]
    public async Task 測定を保存して読み戻すと_解析結果と波形が一致する()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await NewStoreAsync();
        var patient = await store.GetOrCreatePatientAsync(" ａ-１２３ ", "初診", ct);
        Assert.Equal("A-123", patient.LocalId);
        Assert.Equal(pseudonymizer.Pseudonymize("A-123"), patient.PseudonymId);

        var recordings = SimulatedSession();
        var saved = await store.SaveSessionAsync(patient.Id, Hand.Right, "右手", recordings, "1.0.0", cancellationToken: ct);
        Assert.Equal(TremorPattern.RestDominant, saved.Comparison.Pattern);

        var loaded = await store.GetSessionAsync(saved.Id, ct);
        Assert.NotNull(loaded);
        var (summary, waves) = loaded.Value;
        Assert.Equal(saved.Rest!.PeakFrequencyHz, summary.Rest!.PeakFrequencyHz, 1e-9);
        Assert.Equal(saved.Postural!.RmsAccelerationMg, summary.Postural!.RmsAccelerationMg, 1e-9);
        Assert.Equal(recordings[0].Metrics.Bands.Count, summary.Rest.Bands.Count);
        Assert.Equal(2, waves.Count);
        Assert.Equal(recordings[0].Recording.Length, waves[0].Length);
        Assert.Equal(recordings[0].Recording.Axes[2][100], waves[0].Axes[2][100], 1e-5);

        // 同じカルテ番号では新しい患者を作らない
        var again = await store.GetOrCreatePatientAsync("A-123", null, ct);
        Assert.Equal(patient.Id, again.Id);
        Assert.Equal(1, again.SessionCount);
    }

    [Fact]
    public async Task 患者を削除すると測定と波形も消え_クラウド削除用の仮名IDが返る()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await NewStoreAsync();
        var patient = await store.GetOrCreatePatientAsync("P001", null, ct);
        await store.SaveSessionAsync(patient.Id, Hand.Left, null, SimulatedSession(), "1.0.0", cancellationToken: ct);

        string? pseudonym = await store.DeletePatientAsync(patient.Id, ct);

        Assert.Equal(patient.PseudonymId, pseudonym);
        Assert.Empty(await store.ListPatientsAsync(null, ct));
        Assert.Empty(await store.ListSessionsAsync(null, ct));
        await using var db = TremorDbContext.Open(Path.Combine(dir, "test.db"));
        Assert.Equal(0, db.Recordings.Count());
    }

    [Fact]
    public async Task クラウド未送信の一覧は_送信済みにすると減る()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await NewStoreAsync();
        var patient = await store.GetOrCreatePatientAsync("P002", null, ct);
        var s = await store.SaveSessionAsync(patient.Id, Hand.Right, null, SimulatedSession(), "1.0.0", cancellationToken: ct);

        Assert.Contains(s.Id, await store.PendingCloudSyncAsync(ct));
        await store.MarkCloudSyncedAsync(s.Id, ct);
        Assert.DoesNotContain(s.Id, await store.PendingCloudSyncAsync(ct));
        Assert.NotNull((await store.GetDeliveryStatusAsync(s.Id, ct)).Cloud);
    }

    [Fact]
    public async Task クラウドに送る文書にはカルテ番号もメモも入らない()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await NewStoreAsync();
        var patient = await store.GetOrCreatePatientAsync("KARTE-98765", "山田様 個人メモ", ct);
        var s = await store.SaveSessionAsync(patient.Id, Hand.Right, "測定メモ ABC", SimulatedSession(), "1.0.0", cancellationToken: ct);
        var (summary, waves) = (await store.GetSessionAsync(s.Id, ct))!.Value;

        var doc = CloudSync.ToDocument(summary, waves, "1.0.0");
        string json = JsonConvert.SerializeObject(doc);

        Assert.Equal(patient.PseudonymId, doc.PseudonymId);
        Assert.DoesNotContain("KARTE-98765", json, StringComparison.Ordinal);
        Assert.DoesNotContain("山田", json, StringComparison.Ordinal);
        Assert.DoesNotContain("測定メモ", json, StringComparison.Ordinal);
        Assert.Equal(2, doc.Conditions.Count);
        Assert.Equal(3, doc.Conditions[0].Samples.Length);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("HostName=x.azure-devices.net;SharedAccessKeyName=service;SharedAccessKey=abc", false)]
    [InlineData("Endpoint=sb://x.servicebus.windows.net/;SharedAccessKeyName=service;SharedAccessKey=abc", false)]
    [InlineData("Endpoint=sb://x.servicebus.windows.net/;SharedAccessKeyName=service;SharedAccessKey=abc;EntityPath=hub", true)]
    public void 接続文字列の形を確かめる(string value, bool valid)
    {
        var check = ConnectionStringCheck.Inspect(value);
        Assert.Equal(valid, check.IsValid);
        if (valid) Assert.Null(check.Warning);
        else Assert.NotNull(check.Problem);
    }

    [Fact]
    public void すべての権限を持つ鍵には注意を出す()
    {
        var check = ConnectionStringCheck.Inspect("Endpoint=sb://x.servicebus.windows.net/;SharedAccessKeyName=iothubowner;SharedAccessKey=abc;EntityPath=hub");
        Assert.True(check.IsValid);
        Assert.NotNull(check.Warning);
        Assert.DoesNotContain("abc", check.Warning, StringComparison.Ordinal);
    }

    private sealed class XorProtector : ISecretProtector
    {
        public byte[] Protect(byte[] plain) => plain.Select(b => (byte)(b ^ 0x5A)).Prepend((byte)1).ToArray();
        public byte[] Unprotect(byte[] data) => data.Skip(1).Select(b => (byte)(b ^ 0x5A)).ToArray();
    }

    [Fact]
    public void 秘密は暗号化して保存し_設定ファイルには書かない()
    {
        var store = new SettingsStore(dir, new XorProtector());
        store.SaveSettings(new AppSettings { SensorMode = SensorMode.IotHub, IotDeviceId = " dev1 ", MeasurementSeconds = 500 });
        store.SaveSecrets(new AppSecrets { IotHubConnectionString = "Endpoint=sb://secret-host/;SharedAccessKey=TOPSECRET", CosmosKey = "COSMOSKEY" });

        var settings = store.LoadSettings();
        Assert.Equal(SensorMode.IotHub, settings.SensorMode);
        Assert.Equal("dev1", settings.IotDeviceId);
        Assert.Equal(60, settings.MeasurementSeconds);

        Assert.Equal("COSMOSKEY", store.LoadSecrets().CosmosKey);
        string onDisk = File.ReadAllText(store.SecretsPath, Encoding.Latin1) + File.ReadAllText(store.SettingsPath);
        Assert.DoesNotContain("TOPSECRET", onDisk, StringComparison.Ordinal);
        Assert.DoesNotContain("COSMOSKEY", onDisk, StringComparison.Ordinal);
    }

    [Fact]
    public void 施設の鍵は初回に作られ_次からは同じものを使う()
    {
        var store = new SettingsStore(dir, new XorProtector());
        store.SaveSecrets(new AppSecrets { CosmosKey = "keep" });
        byte[] first = store.GetOrCreateSiteKey();
        byte[] second = new SettingsStore(dir, new XorProtector()).GetOrCreateSiteKey();
        Assert.Equal(32, first.Length);
        Assert.Equal(first, second);
        Assert.Equal("keep", store.LoadSecrets().CosmosKey);
    }

    [Fact]
    public void 壊れた設定ファイルは既定値に戻る()
    {
        var store = new SettingsStore(dir, new XorProtector());
        File.WriteAllText(store.SettingsPath, "{ not json");
        Assert.Equal(new AppSettings(), store.LoadSettings());
        Assert.True(File.Exists(store.SettingsPath + ".broken"));
    }
}
