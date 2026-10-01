namespace TremorScope.Core.Domain;

/// <summary>測定の条件</summary>
public enum Condition
{
    /// <summary>安静時: 手を膝の上に置き、力を抜いた状態</summary>
    Rest = 0,

    /// <summary>姿勢時: 両腕を前に伸ばして保った状態</summary>
    Postural = 1,
}

public enum Hand
{
    Right = 0,
    Left = 1,
}

/// <summary>1回の条件で記録した加速度（単位 g）。軸は 1 本（合成値）または 3 本（x, y, z）</summary>
public sealed class Recording
{
    public Recording(Condition condition, double sampleRate, double[][] axes, int missingSamples = 0, DateTime? startedAtUtc = null, string? deviceId = null)
    {
        ArgumentNullException.ThrowIfNull(axes);
        if (axes.Length is not (1 or 3)) throw new ArgumentException("軸は 1 本か 3 本にしてください。", nameof(axes));
        if (axes.Any(a => a.Length != axes[0].Length)) throw new ArgumentException("各軸の点数がそろっていません。", nameof(axes));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        Condition = condition;
        SampleRate = sampleRate;
        Axes = axes;
        MissingSamples = Math.Max(0, missingSamples);
        StartedAtUtc = startedAtUtc ?? DateTime.UtcNow;
        DeviceId = deviceId;
    }

    public Condition Condition { get; }

    public double SampleRate { get; }

    public double[][] Axes { get; }

    /// <summary>通信の途切れなどで届かなかった点の数（送信側の通し番号から数える）</summary>
    public int MissingSamples { get; }

    public DateTime StartedAtUtc { get; }

    public string? DeviceId { get; }

    public int Length => Axes[0].Length;

    public double DurationSeconds => Length / SampleRate;
}
