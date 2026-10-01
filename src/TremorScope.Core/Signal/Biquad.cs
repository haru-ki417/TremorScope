namespace TremorScope.Core.Signal;

/// <summary>
/// 2次の IIR フィルター（バターワース）。重力や手のゆっくりした動き（1Hz 未満）を取り除くのに使う。
/// 係数は Robert Bristow-Johnson の "Audio EQ Cookbook" の式による。
/// </summary>
public sealed class Biquad
{
    private readonly double b0, b1, b2, a1, a2;

    private Biquad(double b0, double b1, double b2, double a0, double a1, double a2)
    {
        this.b0 = b0 / a0;
        this.b1 = b1 / a0;
        this.b2 = b2 / a0;
        this.a1 = a1 / a0;
        this.a2 = a2 / a0;
    }

    /// <summary>高い周波数だけを通す（cutoffHz より遅い動きを除く）</summary>
    public static Biquad HighPass(double cutoffHz, double sampleRate)
    {
        Validate(cutoffHz, sampleRate);
        double w = 2 * Math.PI * cutoffHz / sampleRate, cos = Math.Cos(w), alpha = Math.Sin(w) / (2 * Math.Sqrt(0.5));
        return new Biquad((1 + cos) / 2, -(1 + cos), (1 + cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
    }

    /// <summary>低い周波数だけを通す（cutoffHz より速い成分を除く）</summary>
    public static Biquad LowPass(double cutoffHz, double sampleRate)
    {
        Validate(cutoffHz, sampleRate);
        double w = 2 * Math.PI * cutoffHz / sampleRate, cos = Math.Cos(w), alpha = Math.Sin(w) / (2 * Math.Sqrt(0.5));
        return new Biquad((1 - cos) / 2, 1 - cos, (1 - cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
    }

    public double[] Filter(ReadOnlySpan<double> x)
    {
        var y = new double[x.Length];
        double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
        for (int i = 0; i < x.Length; i++)
        {
            double v = b0 * x[i] + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = x[i];
            y2 = y1; y1 = v;
            y[i] = v;
        }
        return y;
    }

    /// <summary>
    /// 前向きと後ろ向きに2回かける（位相のずれが 0 になり、波形の形が崩れない）。
    /// 端で値が跳ねないよう、両端を折り返した信号を足してからかけ、あとで取り除く。
    /// </summary>
    public double[] FiltFilt(ReadOnlySpan<double> x)
    {
        if (x.Length < 4) return x.ToArray();
        int pad = Math.Min(x.Length - 1, 3 * 50);
        var padded = new double[x.Length + 2 * pad];
        for (int i = 0; i < pad; i++)
        {
            padded[i] = 2 * x[0] - x[pad - i];
            padded[pad + x.Length + i] = 2 * x[^1] - x[x.Length - 2 - i];
        }
        x.CopyTo(padded.AsSpan(pad));

        var forward = Filter(padded);
        Array.Reverse(forward);
        var backward = Filter(forward);
        Array.Reverse(backward);
        return backward.AsSpan(pad, x.Length).ToArray();
    }

    private static void Validate(double cutoffHz, double sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        if (cutoffHz <= 0 || cutoffHz >= sampleRate / 2) throw new ArgumentOutOfRangeException(nameof(cutoffHz), "遮断周波数はサンプリング周波数の半分未満にしてください。");
    }
}
