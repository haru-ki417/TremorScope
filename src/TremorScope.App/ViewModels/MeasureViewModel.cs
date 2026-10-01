using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TremorScope.App.Services;
using TremorScope.Core.Analysis;
using TremorScope.Core.Domain;
using TremorScope.Core.Sensors;
using TremorScope.Infrastructure.Storage;

namespace TremorScope.App.ViewModels;

public enum MeasureStage
{
    Prepare,
    Settling,
    Recording,
    Review,
    Saving,
}

/// <summary>
/// 測定の流れ: 安静時（準備 → 姿勢を整える → 記録 → 確認）→ 姿勢時（同じ）→ 保存して結果へ。
/// センサーからの信号は画面を開いている間ずっと受け取り、記録中だけためる。
/// </summary>
public sealed partial class MeasureViewModel : ObservableObject, IAsyncDisposable
{
    private const double SettleSeconds = 3;
    private readonly MainViewModel main;
    private readonly Dictionary<Condition, StoredRecording> results = [];
    private readonly DispatcherTimer statusTimer;
    private readonly CancellationTokenSource streamCts = new();
    private ISampleSource? source;
    private RecordingBuffer? buffer;
    private DateTime lastPacketAt = DateTime.MinValue;
    private double sampleRate = 50;
    private double baseline = double.NaN;

    public MeasureViewModel(MainViewModel main, PatientListItem patient)
    {
        this.main = main;
        Patient = patient;
        statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        statusTimer.Tick += (_, _) => UpdateSensorStatus();
        statusTimer.Start();
        StartStream();
    }

    public PatientListItem Patient { get; }

    public int DurationSeconds => main.Services.Settings.MeasurementSeconds;

    /// <summary>グラフに足す値（mg、ふるえの成分だけ）</summary>
    public event EventHandler<double[]>? DisplaySamples;

    /// <summary>グラフを消す</summary>
    public event EventHandler? DisplayReset;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstructionTitle), nameof(InstructionBody), nameof(IsRest), nameof(StepIndex), nameof(ConditionLabel))]
    public partial Condition CurrentCondition { get; set; } = Condition.Rest;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPrepare), nameof(IsRunning), nameof(IsReview), nameof(IsSaving), nameof(NextLabel))]
    public partial MeasureStage Stage { get; set; } = MeasureStage.Prepare;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRightHand), nameof(IsLeftHand))]
    public partial Hand Hand { get; set; } = Hand.Right;

    [ObservableProperty]
    public partial string Note { get; set; } = "";

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial string Countdown { get; set; } = "";

    [ObservableProperty]
    public partial string CountdownCaption { get; set; } = "";

    [ObservableProperty]
    public partial string SensorStatus { get; set; } = "センサーに接続しています…";

    [ObservableProperty]
    public partial bool SensorLive { get; set; }

    [ObservableProperty]
    public partial ConditionCard? ReviewCard { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public bool IsRightHand
    {
        get => Hand == Hand.Right;
        set { if (value) Hand = Hand.Right; }
    }

    public bool IsLeftHand
    {
        get => Hand == Hand.Left;
        set { if (value) Hand = Hand.Left; }
    }

    public bool IsRest => CurrentCondition == Condition.Rest;

    public bool IsPrepare => Stage == MeasureStage.Prepare;

    public bool IsRunning => Stage is MeasureStage.Settling or MeasureStage.Recording;

    public bool IsReview => Stage == MeasureStage.Review;

    public bool IsSaving => Stage == MeasureStage.Saving;

    /// <summary>上の段階表示（0 = 安静時, 1 = 姿勢時, 2 = 結果）</summary>
    public int StepIndex => Stage == MeasureStage.Saving ? 2 : CurrentCondition == Condition.Rest ? 0 : 1;

    public string ConditionLabel => Labels.Condition(CurrentCondition);

    public string NextLabel => CurrentCondition == Condition.Rest ? "姿勢時の測定へ" : "保存して結果を見る";

    public string InstructionTitle => CurrentCondition == Condition.Rest
        ? "安静時: 腕の力を抜いてもらいます"
        : "姿勢時: 腕を前に伸ばしてもらいます";

    public string InstructionBody => CurrentCondition == Condition.Rest
        ? "センサーを手の甲に付け、腕を膝または台の上に置いて、力を抜いてもらってください。話したり手を動かしたりしないよう伝えます。"
        : "座ったまま、両腕を肩の高さで前に伸ばし、手のひらを下に向けて保ってもらってください。腕を支えず、姿勢を保つことに集中してもらいます。";

    // ---- センサー

    private void StartStream()
    {
        try
        {
            source = main.Services.CreateSource(() => CurrentCondition);
        }
        catch (InvalidOperationException ex)
        {
            SensorStatus = ex.Message;
            return;
        }
        _ = PumpAsync(source, streamCts.Token);
    }

    private async Task PumpAsync(ISampleSource s, CancellationToken token)
    {
        try
        {
            // 画面のスレッドで受け取る（await の続きが画面のスレッドに戻る）
            await foreach (var packet in s.ReadAsync(token))
            {
                OnPacket(packet);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SensorLive = false;
            SensorStatus = "センサーの受信でエラーが起きました: " + ex.Message;
        }
    }

    private void OnPacket(SensorPacket packet)
    {
        lastPacketAt = DateTime.UtcNow;
        if (packet.SampleRate is double fs) sampleRate = fs;
        DisplaySamples?.Invoke(this, ToDisplay(packet));

        if (buffer is null || !IsRunning) return;
        buffer.Add(packet);
        UpdateProgress();
        if (buffer.IsComplete) Complete();
    }

    /// <summary>
    /// 表示用に「ふるえの成分」だけを取り出す: 3 軸の大きさから、ゆっくり変わる成分（重力・姿勢）を引く。
    /// 解析は保存した生の値で別に行うので、ここは見た目のためだけ。
    /// </summary>
    private double[] ToDisplay(SensorPacket packet)
    {
        double alpha = 1 - Math.Exp(-1 / (sampleRate * 0.4));
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

    private void UpdateSensorStatus()
    {
        if (source is null) return;
        bool live = (DateTime.UtcNow - lastPacketAt).TotalSeconds < 2.5;
        SensorLive = live;
        if (live) SensorStatus = $"受信中 · {sampleRate:0} Hz · {source.Name}";
        else if (!SensorStatus.StartsWith("センサーの受信でエラー", StringComparison.Ordinal))
            SensorStatus = lastPacketAt == DateTime.MinValue ? $"センサーの信号を待っています… （{source.Name}）" : "センサーの信号が途切れています。電源と Wi-Fi を確認してください。";
    }

    private void UpdateProgress()
    {
        if (buffer is null) return;
        Progress = buffer.Progress;
        if (buffer.IsSettling)
        {
            if (Stage != MeasureStage.Settling) Stage = MeasureStage.Settling;
            Countdown = "…";
            CountdownCaption = "姿勢が落ち着くのを待っています";
        }
        else
        {
            if (Stage != MeasureStage.Recording) Stage = MeasureStage.Recording;
            double remaining = Math.Max(0, buffer.DurationSeconds - (buffer.Collected + buffer.MissingSamples) / buffer.SampleRate);
            Countdown = Math.Ceiling(remaining).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            CountdownCaption = "秒 · 記録しています";
        }
    }

    // ---- 操作

    [RelayCommand]
    private void Begin()
    {
        if (!SensorLive)
        {
            ErrorMessage = "センサーからの信号が届いていません。届き始めてから開始してください。";
            return;
        }
        ErrorMessage = null;
        ReviewCard = null;
        buffer = new RecordingBuffer(CurrentCondition, sampleRate, DurationSeconds, SettleSeconds);
        Stage = MeasureStage.Settling;
        Progress = 0;
        Countdown = "…";
        CountdownCaption = "姿勢が落ち着くのを待っています";
        DisplayReset?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Retake() => Begin();

    [RelayCommand]
    private void Abort()
    {
        buffer = null;
        Stage = MeasureStage.Prepare;
        Progress = 0;
    }

    private void Complete()
    {
        var recording = buffer!.ToRecording();
        buffer = null;
        try
        {
            var analysis = TremorAnalyzer.Analyze(recording);
            results[recording.Condition] = new StoredRecording(recording, analysis.Metrics);
            ReviewCard = new ConditionCard(analysis.Metrics);
            Stage = MeasureStage.Review;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            ErrorMessage = "解析できませんでした。もう一度測定してください。（" + ex.Message + "）";
            Stage = MeasureStage.Prepare;
        }
    }

    [RelayCommand]
    private async Task NextAsync()
    {
        if (CurrentCondition == Condition.Rest)
        {
            CurrentCondition = Condition.Postural;
            ReviewCard = null;
            Stage = MeasureStage.Prepare;
            return;
        }

        Stage = MeasureStage.Saving;
        try
        {
            var saved = await main.Services.Store.SaveSessionAsync(Patient.Id, Hand, Note, results.Values.OrderBy(r => r.Recording.Condition).ToList(),
                AppServices.AppVersion, thresholdMg: main.Services.Settings.TremorThresholdMg);
            _ = main.Cloud.SyncPendingAsync();
            await main.RefreshCurrentPatientAsync();
            main.ShowResult(saved.Id);
        }
        catch (Exception ex)
        {
            ErrorMessage = "保存できませんでした: " + ex.Message;
            Stage = MeasureStage.Review;
        }
    }

    /// <summary>安静時の測定だけで終える（姿勢時がとれない場合）</summary>
    [RelayCommand]
    private async Task FinishWithRestOnlyAsync()
    {
        CurrentCondition = Condition.Postural;
        await NextAsync();
    }

    [RelayCommand]
    private void Cancel() => main.ShowPatients();

    public async ValueTask DisposeAsync()
    {
        statusTimer.Stop();
        await streamCts.CancelAsync();
        if (source is not null)
        {
            try
            {
                await source.DisposeAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }
        streamCts.Dispose();
    }
}
