namespace TremorScope.Core.Analysis;

/// <summary>安静時と姿勢時の比べ方の結果（測定値の傾向であり、診断ではない）</summary>
public enum TremorPattern
{
    /// <summary>どちらの条件でも、目立ったふるえは記録されなかった</summary>
    NoClearTremor = 0,

    /// <summary>安静時の方が大きい</summary>
    RestDominant = 1,

    /// <summary>姿勢時の方が大きい</summary>
    PosturalDominant = 2,

    /// <summary>両方で同じくらい</summary>
    Both = 3,

    /// <summary>品質の問題で比べられない</summary>
    Undetermined = 4,
}

public sealed record ConditionComparison(TremorPattern Pattern, double? RestToPosturalRatio, string Summary);

public static class ConditionComparer
{
    /// <summary>「目立ったふるえ」とみなす大きさ（mg, RMS）。設定で変えられる</summary>
    public const double DefaultTremorThresholdMg = 10;

    /// <summary>一方がもう一方のこの倍以上なら「〜の方が大きい」とする</summary>
    public const double DominanceRatio = 1.5;

    public static ConditionComparison Compare(TremorMetrics? rest, TremorMetrics? postural, double thresholdMg = DefaultTremorThresholdMg)
    {
        if (rest is null || postural is null || rest.Quality.Level == QualityLevel.Retake || postural.Quality.Level == QualityLevel.Retake)
            return new ConditionComparison(TremorPattern.Undetermined, null, "両方の条件で有効な測定がそろっていないため、比較できません。");

        double r = rest.RmsAccelerationMg, p = postural.RmsAccelerationMg;
        double? ratio = p > 0 ? r / p : null;

        if (r < thresholdMg && p < thresholdMg)
            return new ConditionComparison(TremorPattern.NoClearTremor, ratio,
                $"どちらの条件でも、ふるえの大きさは基準（{thresholdMg:0} mg）未満でした。");
        if (r >= p * DominanceRatio)
            return new ConditionComparison(TremorPattern.RestDominant, ratio,
                $"安静時のふるえが姿勢時の {r / Math.Max(p, 0.001):0.0} 倍の大きさでした（安静時 {rest.PeakFrequencyHz:0.0} Hz）。");
        if (p >= r * DominanceRatio)
            return new ConditionComparison(TremorPattern.PosturalDominant, ratio,
                $"姿勢時のふるえが安静時の {p / Math.Max(r, 0.001):0.0} 倍の大きさでした（姿勢時 {postural.PeakFrequencyHz:0.0} Hz）。");
        return new ConditionComparison(TremorPattern.Both, ratio,
            $"安静時と姿勢時で、同じくらいの大きさのふるえが記録されました（安静時 {rest.PeakFrequencyHz:0.0} Hz、姿勢時 {postural.PeakFrequencyHz:0.0} Hz）。");
    }
}
