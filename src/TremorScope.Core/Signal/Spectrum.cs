using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace TremorScope.Core.Signal;

/// <summary>周波数ごとの強さ（パワースペクトル密度、単位は g²/Hz）</summary>
public sealed class Spectrum
{
    public Spectrum(double[] frequencies, double[] density)
    {
        ArgumentNullException.ThrowIfNull(frequencies);
        ArgumentNullException.ThrowIfNull(density);
        if (frequencies.Length != density.Length || frequencies.Length < 2) throw new ArgumentException("周波数と強さの数が合いません。");
        Frequencies = frequencies;
        Density = density;
    }

    public double[] Frequencies { get; }

    public double[] Density { get; }

    public double Resolution => Frequencies[1] - Frequencies[0];

    /// <summary>[low, high] Hz の範囲の強さの合計（g²）。範囲の端の区間は重なった割合だけ数える</summary>
    public double BandPower(double low, double high)
    {
        double df = Resolution, sum = 0;
        for (int i = 0; i < Frequencies.Length; i++)
        {
            double binLow = Frequencies[i] - df / 2, binHigh = Frequencies[i] + df / 2;
            double overlap = Math.Min(binHigh, high) - Math.Max(binLow, low);
            if (overlap > 0) sum += Density[i] * overlap;
        }
        return sum;
    }

    /// <summary>[low, high] Hz で最も強い周波数。となりの値との放物線補間で、分解能より細かく求める</summary>
    public (double Frequency, double Density) Peak(double low, double high)
    {
        int best = -1;
        for (int i = 0; i < Frequencies.Length; i++)
        {
            if (Frequencies[i] < low || Frequencies[i] > high) continue;
            if (best < 0 || Density[i] > Density[best]) best = i;
        }
        if (best < 0) return (double.NaN, 0);
        if (best == 0 || best == Frequencies.Length - 1) return (Frequencies[best], Density[best]);

        double a = Density[best - 1], b = Density[best], c = Density[best + 1];
        double denominator = a - 2 * b + c;
        double shift = denominator == 0 ? 0 : Math.Clamp(0.5 * (a - c) / denominator, -0.5, 0.5);
        return (Frequencies[best] + shift * Resolution, b - 0.25 * (a - c) * shift);
    }

    /// <summary>複数の軸の強さを足し合わせる（3軸センサーの場合）</summary>
    public static Spectrum Sum(IReadOnlyList<Spectrum> spectra)
    {
        ArgumentNullException.ThrowIfNull(spectra);
        if (spectra.Count == 0) throw new ArgumentException("1つ以上必要です。", nameof(spectra));
        var density = new double[spectra[0].Density.Length];
        foreach (var s in spectra)
        {
            if (s.Density.Length != density.Length) throw new ArgumentException("周波数の刻みがそろっていません。", nameof(spectra));
            for (int i = 0; i < density.Length; i++) density[i] += s.Density[i];
        }
        return new Spectrum(spectra[0].Frequencies, density);
    }

    /// <summary>
    /// ウェルチ法: 信号を半分ずつ重なる区間に分け、それぞれに窓（ハン窓）をかけて周波数分析し、平均する。
    /// 1回で分析するよりノイズが小さく、安定した結果になる。
    /// </summary>
    public static Spectrum Welch(ReadOnlySpan<double> signal, double sampleRate, int segmentLength = 256)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        if (signal.Length < 32) throw new ArgumentException("分析には 32 点以上が必要です。", nameof(signal));

        int n = Math.Min(segmentLength, HighestPowerOfTwo(signal.Length));
        int step = n / 2;
        var window = new double[n];
        double windowPower = 0;
        for (int i = 0; i < n; i++)
        {
            window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1));
            windowPower += window[i] * window[i];
        }

        int bins = n / 2 + 1;
        var density = new double[bins];
        var buffer = new Complex[n];
        int segments = 0;
        for (int start = 0; start + n <= signal.Length; start += step)
        {
            double mean = 0;
            for (int i = 0; i < n; i++) mean += signal[start + i];
            mean /= n;
            for (int i = 0; i < n; i++) buffer[i] = new Complex((signal[start + i] - mean) * window[i], 0);
            Fourier.Forward(buffer, FourierOptions.Matlab);
            for (int k = 0; k < bins; k++) density[k] += buffer[k].Real * buffer[k].Real + buffer[k].Imaginary * buffer[k].Imaginary;
            segments++;
        }

        // 片側スペクトルにする（直流とナイキスト以外は 2 倍）
        double scale = 1.0 / (sampleRate * windowPower * segments);
        var frequencies = new double[bins];
        for (int k = 0; k < bins; k++)
        {
            density[k] *= scale * (k == 0 || k == bins - 1 ? 1 : 2);
            frequencies[k] = k * sampleRate / n;
        }
        return new Spectrum(frequencies, density);
    }

    private static int HighestPowerOfTwo(int value)
    {
        int p = 1;
        while (p * 2 <= value) p *= 2;
        return p;
    }
}
