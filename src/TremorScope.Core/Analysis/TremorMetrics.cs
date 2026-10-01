using TremorScope.Core.Domain;

namespace TremorScope.Core.Analysis;

/// <summary>周波数帯ごとの強さの割合</summary>
public sealed record BandShare(string Label, double LowHz, double HighHz, double Share);

/// <summary>測定の品質</summary>
public enum QualityLevel
{
    /// <summary>問題なし</summary>
    Good = 0,

    /// <summary>結果は出せるが、注意が必要</summary>
    Caution = 1,

    /// <summary>測り直しが必要</summary>
    Retake = 2,
}

public sealed record QualityReport(QualityLevel Level, IReadOnlyList<string> Messages)
{
    public static QualityReport Ok { get; } = new(QualityLevel.Good, []);
}

/// <summary>
/// 1回の測定（1つの条件）の解析結果。
/// どれも波形から計算した客観的な数値で、病気の有無を判断するものではない。
/// </summary>
public sealed record TremorMetrics(
    Condition Condition,
    // ふるえの帯域（2〜15Hz）で最も強い周波数（Hz）
    double PeakFrequencyHz,
    // ふるえの帯域の加速度の大きさ（RMS、単位 mg = 1/1000 g）
    double RmsAccelerationMg,
    // 加速度から推定した変位の大きさ（RMS、mm）。あくまで目安
    double EstimatedDisplacementMm,
    // 最も強い周波数の ±1Hz に、帯域の強さのどれだけが集まっているか（0〜1）。規則的なふるえほど大きい
    double Regularity,
    IReadOnlyList<BandShare> Bands,
    QualityReport Quality,
    double DurationSeconds,
    string AlgorithmVersion)
{
    /// <summary>4〜6Hz の割合（安静時振戦でよく見られる帯域）</summary>
    public double Share4To6 => Bands.FirstOrDefault(b => b.LowHz == 4 && b.HighHz == 6)?.Share ?? 0;
}
