using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.JSInterop;
using TremorScope.Core.Analysis;
using TremorScope.Core.Domain;
using TremorScope.Core.Export;
using TremorScope.Core.Sensors;

namespace TremorScope.Web.State;

public enum Page
{
    Measure,
    Result,
    History,
    About,
}

public enum Stage
{
    /// <summary>始める前（説明を読む）</summary>
    Prepare,

    /// <summary>始まるまでの数秒（スマホを手に乗せて姿勢をとる時間）</summary>
    Countdown,

    /// <summary>姿勢が落ち着くまで（記録しない）</summary>
    Settling,

    /// <summary>記録中</summary>
    Recording,

    /// <summary>記録の品質と結果を確かめる</summary>
    Review,
}

public enum SensorKind
{
    /// <summary>このスマホ・タブレットの加速度センサー</summary>
    Phone,

    /// <summary>見本の信号（センサーがなくても操作を試せる）</summary>
    Simulated,
}

/// <summary>
/// 画面全体の状態。測定の流れは Windows 版と同じ:
/// 安静時（準備 → 始まるまで → 姿勢を整える → 記録 → 確認）→ 姿勢時（同じ）→ 保存して結果へ。
/// 記録はこのブラウザーの中（localStorage）だけに保存する。
/// </summary>
public sealed class Lab(BrowserIo io)
{
    private const string SessionsKey = "tremorscope.sessions.v1";
    private const string SettingsKey = "tremorscope.settings.v1";
    private const double SettleSeconds = 3;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly Dictionary<Condition, Recording> results = [];
    private DotNetObjectReference<Lab>? self;
    private CancellationTokenSource? simCts;
    private RecordingBuffer? buffer;
    private DateTime lastPacketAt = DateTime.MinValue;
    private DateTime countdownEnd;
    private double baseline = double.NaN;
    private bool started;

    public event Action? Changed;

    /// <summary>グラフに足す値（mg、ふるえの成分だけ）</summary>
    public event Action<double[]>? Display;

    /// <summary>グラフを消す</summary>
    public event Action? DisplayReset;

    public void Notify() => Changed?.Invoke();

    // ---- 画面

    public Page Page { get; private set; } = Page.Measure;

    public Session? Current { get; private set; }

    public async Task GoAsync(Page page)
    {
        if (page == Page.Result && Current is null) page = Page.History;
        Page = page;
        Notify();
        await io.ScrollTopAsync();
    }

    public async Task OpenAsync(Session session)
    {
        Current = session;
        await GoAsync(Page.Result);
    }

    // ---- 設定（この端末に保存）

    public int MeasurementSeconds { get; private set; } = 20;

    public double ThresholdMg { get; private set; } = ConditionComparer.DefaultTremorThresholdMg;

    public int PrepareSeconds { get; private set; } = 5;

    public async Task SetSettingsAsync(int seconds, double thresholdMg, int prepareSeconds)
    {
        MeasurementSeconds = Math.Clamp(seconds, 10, 60);
        ThresholdMg = Math.Clamp(thresholdMg, 1, 200);
        PrepareSeconds = Math.Clamp(prepareSeconds, 0, 15);
        await io.SaveAsync(SettingsKey, JsonSerializer.Serialize(new StoredSettings(MeasurementSeconds, ThresholdMg, PrepareSeconds), Json));
        Notify();
    }

    // ---- 記録の一覧

    public List<Session> Sessions { get; } = [];

    public string? StorageWarning { get; private set; }

    /// <summary>はじめに 1 回だけ: 保存した設定と記録を読み、センサーの状態を調べる</summary>
    public async Task StartAsync()
    {
        if (started) return;
        started = true;
        self = DotNetObjectReference.Create(this);
        try
        {
            if (await io.LoadAsync(SettingsKey) is { Length: > 0 } st && JsonSerializer.Deserialize<StoredSettings>(st, Json) is { } s)
            {
                MeasurementSeconds = Math.Clamp(s.MeasurementSeconds, 10, 60);
                ThresholdMg = Math.Clamp(s.ThresholdMg, 1, 200);
                PrepareSeconds = Math.Clamp(s.PrepareSeconds, 0, 15);
            }
            if (await io.LoadAsync(SessionsKey) is { Length: > 0 } json && JsonSerializer.Deserialize<List<StoredSession>>(json, Json) is { } list)
            {
                foreach (var stored in list)
                {
                    try
                    {
                        Sessions.Add(Session.FromStored(stored));
                    }
                    catch (Exception ex) when (ex is ArgumentException or FormatException)
                    {
                        StorageWarning = "読めない記録がありました（その記録は表示していません）。";
                    }
                }
                Sessions.Sort((a, b) => b.MeasuredAtUtc.CompareTo(a.MeasuredAtUtc));
            }
        }
        catch (JsonException)
        {
            StorageWarning = "保存した記録を読めませんでした。";
        }

        PhoneSupported = await io.MotionSupportedAsync();
        PhoneNeedsPermission = PhoneSupported && await io.MotionNeedsPermissionAsync();
        if (PhoneSupported && !PhoneNeedsPermission)
        {
            Sensor = SensorKind.Phone;
            await io.StartMotionAsync(self);
        }
        else if (!PhoneSupported)
        {
            UseSimulation(SimulationProfile.RestTremor);
        }
        _ = TickAsync();
        Notify();
    }

    private async Task PersistAsync()
    {
        var json = JsonSerializer.Serialize(Sessions.Select(s => s.ToStored()).ToList(), Json);
        StorageWarning = await io.SaveAsync(SessionsKey, json)
            ? null
            : "この端末に保存できませんでした（ブラウザーの保存容量がいっぱいか、保存が許可されていません）。古い記録を消すか、CSV で書き出してください。";
    }

    public async Task DeleteAsync(Session session)
    {
        Sessions.Remove(session);
        if (Current == session) Current = null;
        await PersistAsync();
        if (Page == Page.Result && Current is null) Page = Page.History;
        Notify();
    }

    public async Task DeleteAllAsync()
    {
        Sessions.Clear();
        Current = null;
        await PersistAsync();
        Notify();
    }

    /// <summary>見本の信号で 1 回分の測定を作る（画面を試すため）</summary>
    public async Task AddDemoAsync(SimulationProfile profile)
    {
        int seed = Environment.TickCount & 0xFFFF;
        var recordings = new[] { Condition.Rest, Condition.Postural }
            .Select(c => SimulatedSource.CreateRecording(profile, c, MeasurementSeconds, seed + (int)c));
        string note = profile switch
        {
            SimulationProfile.RestTremor => "見本: 安静時に目立つふるえ",
            SimulationProfile.PosturalTremor => "見本: 姿勢時に目立つふるえ",
            _ => "見本: 目立ったふるえなし",
        };
        var session = new Session(Guid.NewGuid(), "見本", Hand.Right, note, DateTime.UtcNow, ThresholdMg, "見本の信号", recordings);
        Sessions.Insert(0, session);
        await PersistAsync();
        await OpenAsync(session);
    }

    // ---- 測定の前に入れること

    public string Label { get; set; } = "";

    public Hand Hand { get; set; } = Hand.Right;

    public string Note { get; set; } = "";

    /// <summary>これまでに使った名前（入力の候補）</summary>
    public IEnumerable<string> KnownLabels => Sessions.Select(s => s.Label).Where(l => !string.IsNullOrWhiteSpace(l)).Distinct();

    // ---- センサー

    public SensorKind Sensor { get; private set; } = SensorKind.Phone;

    public SimulationProfile Profile { get; private set; } = SimulationProfile.RestTremor;

    public bool PhoneSupported { get; private set; }

    /// <summary>iPhone・iPad では、使う前に許可を求める</summary>
    public bool PhoneNeedsPermission { get; private set; }

    public bool PhoneGranted { get; private set; }

    public bool SensorLive { get; private set; }

    public string SensorStatus { get; private set; } = "センサーを準備しています…";

    public double SampleRate { get; private set; } = 50;

    /// <summary>端末のセンサーが実際に値を出している間隔（Hz）</summary>
    public int NativeRate { get; private set; }

    public bool IsRunning => Stage is Stage.Countdown or Stage.Settling or Stage.Recording;

    public async Task UsePhoneAsync()
    {
        if (IsRunning) return;
        StopSimulation();
        Sensor = SensorKind.Phone;
        lastPacketAt = DateTime.MinValue;
        if (!PhoneNeedsPermission || PhoneGranted) await io.StartMotionAsync(self!);
        Notify();
    }

    public void UseSimulation(SimulationProfile profile)
    {
        if (IsRunning) return;
        Sensor = SensorKind.Simulated;
        Profile = profile;
        _ = io.StopMotionAsync().AsTask();
        StopSimulation();
        var cts = simCts = new CancellationTokenSource();
        var source = new SimulatedSource(profile, () => CurrentCondition);
        _ = PumpAsync(source, cts.Token);
        Notify();
    }

    private void StopSimulation()
    {
        simCts?.Cancel();
        simCts = null;
    }

    private async Task PumpAsync(SimulatedSource source, CancellationToken token)
    {
        try
        {
            await foreach (var packet in source.ReadAsync(token)) OnPacketCore(packet);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>iPhone・iPad の許可の結果（JS から）</summary>
    [JSInvokable]
    public void OnPermission(bool granted)
    {
        PhoneGranted = granted;
        if (granted)
        {
            StopSimulation();
            Sensor = SensorKind.Phone;
        }
        else
        {
            SensorStatus = "センサーの使用が許可されませんでした。設定アプリの Safari →「モーションと画面の向きのアクセス」を確認してください。";
        }
        Notify();
    }

    /// <summary>スマホのセンサーの値（50 Hz、単位 g）。0.5 秒ごとに JS から届く</summary>
    [JSInvokable]
    public void OnPacket(double[] x, double[] y, double[] z, long sequence, int nativeRate)
    {
        if (Sensor != SensorKind.Phone) return;
        NativeRate = nativeRate;
        OnPacketCore(new SensorPacket([x, y, z], "browser", sequence, 50, DateTimeOffset.UtcNow));
    }

    private void OnPacketCore(SensorPacket packet)
    {
        lastPacketAt = DateTime.UtcNow;
        if (packet.SampleRate is double fs) SampleRate = fs;
        if (packet.Count == 0) return;
        Display?.Invoke(ToDisplay(packet));
        if (buffer is null || Stage is not (Stage.Settling or Stage.Recording)) return;
        buffer.Add(packet);
        UpdateProgress();
        if (buffer.IsComplete) _ = CompleteAsync();
        else Notify();
    }

    /// <summary>表示用に「ふるえの成分」だけを取り出す（3 軸の大きさから、ゆっくり変わる成分を引く）。解析は生の値で別に行う</summary>
    private double[] ToDisplay(SensorPacket packet)
    {
        double alpha = 1 - Math.Exp(-1 / (SampleRate * 0.4));
        var shown = new double[packet.Count];
        for (int i = 0; i < packet.Count; i++)
        {
            double m = 0;
            foreach (var axis in packet.Axes) m += axis[i] * axis[i];
            m = packet.Axes.Length == 1 ? packet.Axes[0][i] : Math.Sqrt(m);
            baseline = double.IsNaN(baseline) ? m : baseline + alpha * (m - baseline);
            shown[i] = (m - baseline) * 1000;
        }
        return shown;
    }

    /// <summary>0.25 秒ごと: センサーの状態と、始まるまでの秒読み</summary>
    private async Task TickAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (await timer.WaitForNextTickAsync())
        {
            bool live = (DateTime.UtcNow - lastPacketAt).TotalSeconds < 2;
            string status = SensorStatusText(live);
            bool changed = live != SensorLive || status != SensorStatus;
            SensorLive = live;
            SensorStatus = status;
            if (Stage == Stage.Countdown)
            {
                double remaining = (countdownEnd - DateTime.UtcNow).TotalSeconds;
                if (remaining <= 0)
                {
                    BeginRecording();
                }
                else
                {
                    Countdown = Math.Ceiling(remaining).ToString("0", CultureInfo.InvariantCulture);
                }
                changed = true;
            }
            if (IsRunning && !live && Stage != Stage.Countdown)
            {
                Abort();
                Error = "センサーの値が途切れたため、測定を中止しました。画面を消したり、ほかのアプリに切りかえたりしないでください。";
                changed = true;
            }
            if (changed) Notify();
        }
    }

    private string SensorStatusText(bool live)
    {
        if (Sensor == SensorKind.Simulated)
            return live ? Profile switch
            {
                SimulationProfile.RestTremor => "見本の信号: 安静時に目立つふるえ",
                SimulationProfile.PosturalTremor => "見本の信号: 姿勢時に目立つふるえ",
                _ => "見本の信号: 目立ったふるえなし",
            } : "見本の信号を準備しています…";
        if (!PhoneSupported) return "この端末には加速度センサーがありません。見本の信号で操作を試せます。";
        if (PhoneNeedsPermission && !PhoneGranted) return "「センサーを使う」を押して、加速度センサーの使用を許可してください。";
        if (live)
        {
            string rate = NativeRate > 0 ? $"（センサー {NativeRate} Hz → 50 Hz にそろえて記録）" : "";
            return "このスマホのセンサーで受信中" + rate;
        }
        return lastPacketAt == DateTime.MinValue
            ? "センサーの値を待っています…（スマホを少し動かしてみてください）"
            : "センサーの値が途切れています。";
    }

    /// <summary>センサーが遅すぎる（ふるえの 15 Hz まで測るには 30 Hz より速い必要がある）</summary>
    public bool SensorTooSlow => Sensor == SensorKind.Phone && NativeRate is > 0 and < 35;

    // ---- 測定の流れ

    public Condition CurrentCondition { get; private set; } = Condition.Rest;

    public Stage Stage { get; private set; } = Stage.Prepare;

    public double Progress { get; private set; }

    public string Countdown { get; private set; } = "";

    public string CountdownCaption { get; private set; } = "";

    public AnalysisResult? Review { get; private set; }

    public string? Error { get; set; }

    public bool HasRest => results.ContainsKey(Condition.Rest);

    /// <summary>上の段階表示（0 = 安静時, 1 = 姿勢時）</summary>
    public int StepIndex => CurrentCondition == Condition.Rest ? 0 : 1;

    public async Task BeginAsync()
    {
        if (!SensorLive)
        {
            Error = Sensor == SensorKind.Phone && PhoneNeedsPermission && !PhoneGranted
                ? "先に「センサーを使う」を押して、センサーの使用を許可してください。"
                : "センサーの値が届いていません。届き始めてから開始してください。";
            Notify();
            return;
        }
        Error = null;
        Review = null;
        results.Remove(CurrentCondition); // 取り直す場合、前の結果は使わない
        buffer = null;
        Progress = 0;
        DisplayReset?.Invoke();
        await io.KeepAwakeAsync(true);
        int prepare = Sensor == SensorKind.Phone ? PrepareSeconds : 0;
        if (prepare > 0)
        {
            Stage = Stage.Countdown;
            countdownEnd = DateTime.UtcNow.AddSeconds(prepare);
            Countdown = prepare.ToString(CultureInfo.InvariantCulture);
            CountdownCaption = "秒後に始まります";
        }
        else
        {
            BeginRecording();
        }
        Notify();
    }

    private void BeginRecording()
    {
        buffer = new RecordingBuffer(CurrentCondition, SampleRate, MeasurementSeconds, SettleSeconds);
        Stage = Stage.Settling;
        Countdown = "…";
        CountdownCaption = "姿勢が落ち着くのを待っています";
        _ = io.BuzzAsync(120).AsTask();
    }

    private void UpdateProgress()
    {
        if (buffer is null) return;
        Progress = buffer.Progress;
        if (buffer.IsSettling)
        {
            Stage = Stage.Settling;
            Countdown = "…";
            CountdownCaption = "姿勢が落ち着くのを待っています";
        }
        else
        {
            Stage = Stage.Recording;
            double remaining = Math.Max(0, buffer.DurationSeconds - (buffer.Collected + buffer.MissingSamples) / buffer.SampleRate);
            Countdown = Math.Ceiling(remaining).ToString("0", CultureInfo.InvariantCulture);
            CountdownCaption = "秒 · 記録しています";
        }
    }

    public void Abort()
    {
        buffer = null;
        results.Remove(CurrentCondition);
        Stage = Stage.Prepare;
        Progress = 0;
        _ = io.KeepAwakeAsync(false).AsTask();
        Notify();
    }

    private async Task CompleteAsync()
    {
        var recording = buffer!.ToRecording();
        buffer = null;
        await io.KeepAwakeAsync(false);
        await io.BuzzAsync(300);
        try
        {
            Review = TremorAnalyzer.Analyze(recording);
            results[recording.Condition] = recording;
            Stage = Stage.Review;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            Error = "解析できませんでした。もう一度測定してください。（" + ex.Message + "）";
            Stage = Stage.Prepare;
        }
        Notify();
    }

    public async Task NextAsync()
    {
        if (CurrentCondition == Condition.Rest)
        {
            CurrentCondition = Condition.Postural;
            Review = null;
            Stage = Stage.Prepare;
            DisplayReset?.Invoke();
            Notify();
            await io.ScrollTopAsync();
            return;
        }
        await SaveAsync();
    }

    /// <summary>安静時の測定だけで終える（姿勢時がとれない場合）</summary>
    public async Task FinishWithRestOnlyAsync()
    {
        results.Remove(Condition.Postural);
        await SaveAsync();
    }

    private async Task SaveAsync()
    {
        string source = Sensor == SensorKind.Phone ? "このスマホのセンサー" : "見本の信号";
        var session = new Session(Guid.NewGuid(), Label.Trim(), Hand, Note.Trim(), DateTime.UtcNow, ThresholdMg, source, results.Values.OrderBy(r => r.Condition));
        Sessions.Insert(0, session);
        await PersistAsync();
        ResetFlow();
        await OpenAsync(session);
    }

    /// <summary>測定を最初（安静時）からやり直す</summary>
    public void ResetFlow()
    {
        results.Clear();
        buffer = null;
        Review = null;
        CurrentCondition = Condition.Rest;
        Stage = Stage.Prepare;
        Progress = 0;
        Error = null;
        DisplayReset?.Invoke();
        _ = io.KeepAwakeAsync(false).AsTask();
        Notify();
    }

    // ---- 書き出し

    public async Task ExportSessionsCsvAsync(IEnumerable<Session> sessions, string name)
    {
        var csv = CsvExporter.Sessions(sessions.OrderBy(s => s.MeasuredAtUtc).Select(s => s.ToSummary()), includeLocalId: true);
        await io.DownloadAsync(name, "text/csv", CsvExporter.Encoding.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray());
    }

    public async Task ExportWaveformAsync(Session session, Condition condition)
    {
        if (session.RecordingOf(condition) is not { } r) return;
        var csv = CsvExporter.Waveform(r);
        string name = $"tremorscope_{session.MeasuredAtUtc.ToLocalTime():yyyyMMdd_HHmm}_{Labels.ConditionKey(condition)}.csv";
        await io.DownloadAsync(name, "text/csv", CsvExporter.Encoding.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray());
    }

    public ValueTask PrintAsync() => io.PrintAsync();

    public ValueTask BindPermissionButtonAsync(string id) => io.BindPermissionButtonAsync(id, self!);
}
