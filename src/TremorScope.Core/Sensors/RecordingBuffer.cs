using TremorScope.Core.Domain;

namespace TremorScope.Core.Sensors;

/// <summary>
/// 届いた値をためて、1 回分の記録（Recording）にする。
/// はじめの数秒は、姿勢を整えている間の動きが入りやすいので捨てる。
/// </summary>
public sealed class RecordingBuffer
{
    private readonly List<double>[] axes;
    private readonly double settleSeconds;
    private long? nextSequence;
    private int skipped;
    private int axisCount;

    public RecordingBuffer(Condition condition, double sampleRate, double durationSeconds, double settleSeconds = 2)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(durationSeconds);
        Condition = condition;
        SampleRate = sampleRate;
        DurationSeconds = durationSeconds;
        this.settleSeconds = Math.Max(0, settleSeconds);
        axes = [new(), new(), new()];
        StartedAtUtc = DateTime.UtcNow;
    }

    public Condition Condition { get; }

    public double SampleRate { get; private set; }

    public double DurationSeconds { get; }

    public DateTime StartedAtUtc { get; private set; }

    public string? DeviceId { get; private set; }

    /// <summary>通し番号の飛びから数えた、届かなかった点の数（記録に使う区間のみ）</summary>
    public int MissingSamples { get; private set; }

    /// <summary>この秒数以内の「戻り」は重複として捨てる</summary>
    private const double DuplicateWindowSeconds = 5;

    private int SettleSamples => (int)Math.Round(settleSeconds * SampleRate);

    private int TargetSamples => (int)Math.Round(DurationSeconds * SampleRate);

    /// <summary>記録した点の数（落ち着くまでの区間は含めない）</summary>
    public int Collected => axisCount == 0 ? 0 : axes[0].Count;

    /// <summary>0〜1 の進み具合（落ち着くまでの区間を含めた全体に対して）</summary>
    public double Progress => Math.Clamp((double)(skipped + Collected + MissingSamples) / (SettleSamples + TargetSamples), 0, 1);

    /// <summary>落ち着くまでの区間（まだ記録していない）か</summary>
    public bool IsSettling => skipped < SettleSamples;

    public bool IsComplete => Collected + MissingSamples >= TargetSamples;

    /// <summary>値を追加する。追加した点のうち、記録に使った点の値（グラフ表示用、最初の軸）を返す</summary>
    public IReadOnlyList<double> Add(SensorPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (IsComplete || packet.Count == 0) return [];
        if (axisCount == 0)
        {
            axisCount = packet.Axes.Length;
            if (packet.SampleRate is double fs) SampleRate = fs;
            DeviceId = packet.DeviceId;
            StartedAtUtc = DateTime.UtcNow;
        }
        if (packet.Axes.Length != axisCount) return []; // 途中で形式が変わったものは使わない

        // 通し番号が飛んでいたら、その分を「届かなかった点」として数える
        if (packet.Sequence is long seq)
        {
            if (nextSequence is long expected && seq > expected)
            {
                long gap = seq - expected;
                // 落ち着くまでの区間に入る分は捨てる区間として数え、残りを欠けとして数える（記録の残りの長さを超えては数えない）
                int toSettle = (int)Math.Min(gap, SettleSamples - skipped);
                skipped += toSettle;
                long remaining = TargetSamples - (Collected + MissingSamples);
                MissingSamples += (int)Math.Clamp(gap - toSettle, 0, Math.Max(remaining, 0));
                if (IsComplete)
                {
                    nextSequence = seq + packet.Count;
                    return [];
                }
            }
            else if (nextSequence is long e2 && seq < e2)
            {
                // 少し戻っただけなら重複（再送）として捨てる。大きく戻ったらセンサーが再起動したとみなし、番号を数え直す
                if (e2 - seq <= SampleRate * DuplicateWindowSeconds) return [];
            }
            nextSequence = seq + packet.Count;
        }

        var shown = new List<double>();
        for (int i = 0; i < packet.Count && !IsComplete; i++)
        {
            if (skipped < SettleSamples)
            {
                skipped++;
                continue;
            }
            for (int a = 0; a < axisCount; a++) axes[a].Add(packet.Axes[a][i]);
            shown.Add(packet.Axes[0][i]);
        }
        return shown;
    }

    public Recording ToRecording() =>
        new(Condition, SampleRate, axes.Take(Math.Max(axisCount, 1)).Select(a => a.ToArray()).ToArray(), MissingSamples, StartedAtUtc, DeviceId);
}
