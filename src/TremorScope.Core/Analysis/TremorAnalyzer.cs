using TremorScope.Core.Domain;
using TremorScope.Core.Signal;

namespace TremorScope.Core.Analysis;

/// <summary>解析の結果と、画面のグラフに使う信号・スペクトル</summary>
public sealed record AnalysisResult(TremorMetrics Metrics, Spectrum Spectrum, double[] FilteredSignal);

/// <summary>
/// 加速度の波形から、ふるえの周波数・大きさ・規則性を求める。
///
/// 手順:
///   1. 各軸の平均を引き、1Hz の高域通過フィルターで重力と手のゆっくりした動きを除く
///   2. 各軸をウェルチ法で周波数分析し、足し合わせる（センサーの向きに左右されないように）
///   3. 2〜15Hz（ふるえの帯域）で、最も強い周波数・強さの合計・帯域ごとの割合を求める
/// </summary>
public static class TremorAnalyzer
{
    public const string AlgorithmVersion = "1.0";

    public const double TremorBandLowHz = 2;
    public const double TremorBandHighHz = 15;

    private const double HighPassHz = 1.0;
    private const double StandardGravity = 9.80665;

    /// <summary>割合を表示する帯域</summary>
    public static readonly IReadOnlyList<(string Label, double Low, double High)> ReportBands =
    [
        ("2〜4 Hz", 2, 4),
        ("4〜6 Hz", 4, 6),
        ("6〜8 Hz", 6, 8),
        ("8〜12 Hz", 8, 12),
        ("12〜15 Hz", 12, 15),
    ];

    public static AnalysisResult Analyze(Recording recording)
    {
        ArgumentNullException.ThrowIfNull(recording);
        if (recording.Length < 64) throw new ArgumentException("解析には 64 点以上が必要です。", nameof(recording));

        double fs = recording.SampleRate;
        var highPass = Biquad.HighPass(HighPassHz, fs);
        var filtered = recording.Axes.Select(axis => highPass.FiltFilt(RemoveMean(axis))).ToArray();

        // 1回の分析区間はおよそ 5 秒（50Hz なら 256 点、分解能 0.2Hz）
        int segment = NextPowerOfTwo((int)Math.Round(fs * 5));
        var spectrum = Spectrum.Sum(filtered.Select(axis => Spectrum.Welch(axis, fs, segment)).ToList());

        double upper = Math.Min(TremorBandHighHz, fs / 2 * 0.95);
        double bandPower = spectrum.BandPower(TremorBandLowHz, upper);
        var (peakHz, _) = spectrum.Peak(TremorBandLowHz, upper);
        double regularity = bandPower <= 0 ? 0 : spectrum.BandPower(Math.Max(TremorBandLowHz, peakHz - 1), Math.Min(upper, peakHz + 1)) / bandPower;

        var bands = ReportBands
            .Where(b => b.Low < upper)
            .Select(b => new BandShare(b.Label, b.Low, b.High, bandPower <= 0 ? 0 : spectrum.BandPower(b.Low, Math.Min(b.High, upper)) / bandPower))
            .ToList();

        var metrics = new TremorMetrics(
            recording.Condition,
            PeakFrequencyHz: peakHz,
            RmsAccelerationMg: Math.Sqrt(bandPower) * 1000,
            EstimatedDisplacementMm: DisplacementRmsMm(spectrum, TremorBandLowHz, upper),
            Regularity: Math.Clamp(regularity, 0, 1),
            Bands: bands,
            Quality: QualityChecker.Check(recording, filtered, spectrum),
            DurationSeconds: recording.DurationSeconds,
            AlgorithmVersion: AlgorithmVersion);

        return new AnalysisResult(metrics, spectrum, DominantAxis(filtered));
    }

    /// <summary>
    /// 加速度のスペクトルを (2πf)⁴ で割ると変位のスペクトルになる。
    /// 単位を g から m/s² に直し、合計の平方根を mm で返す。
    /// </summary>
    internal static double DisplacementRmsMm(Spectrum spectrum, double low, double high)
    {
        double df = spectrum.Resolution, sum = 0;
        for (int i = 0; i < spectrum.Frequencies.Length; i++)
        {
            double f = spectrum.Frequencies[i];
            double overlap = Math.Min(f + df / 2, high) - Math.Max(f - df / 2, low);
            if (overlap <= 0 || f <= 0) continue;
            double w = Math.Pow(2 * Math.PI * f, 4);
            sum += spectrum.Density[i] * StandardGravity * StandardGravity / w * overlap;
        }
        return Math.Sqrt(sum) * 1000;
    }

    /// <summary>3軸のとき、ふるえが最も大きい向きの成分（主成分）を返す。グラフ表示用</summary>
    internal static double[] DominantAxis(double[][] axes)
    {
        if (axes.Length == 1) return axes[0];

        int n = axes[0].Length;
        var cov = new double[3, 3];
        for (int i = 0; i < 3; i++)
            for (int j = i; j < 3; j++)
            {
                double s = 0;
                for (int k = 0; k < n; k++) s += axes[i][k] * axes[j][k];
                cov[i, j] = cov[j, i] = s / n;
            }

        // べき乗法で最大固有ベクトルを求める
        double[] v = [1, 1, 1];
        for (int iter = 0; iter < 50; iter++)
        {
            double[] next = new double[3];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++) next[i] += cov[i, j] * v[j];
            double norm = Math.Sqrt(next.Sum(x => x * x));
            if (norm == 0) return axes[0];
            v = next.Select(x => x / norm).ToArray();
        }

        var result = new double[n];
        for (int k = 0; k < n; k++) result[k] = v[0] * axes[0][k] + v[1] * axes[1][k] + v[2] * axes[2][k];
        return result;
    }

    internal static double[] RemoveMean(double[] x)
    {
        double mean = x.Average();
        return x.Select(v => v - mean).ToArray();
    }

    private static int NextPowerOfTwo(int value)
    {
        int p = 1;
        while (p < value) p *= 2;
        return p;
    }
}
