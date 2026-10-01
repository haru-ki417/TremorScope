using System.Globalization;
using TremorScope.Core.Analysis;
using TremorScope.Core.Domain;

namespace TremorScope.App.ViewModels;

/// <summary>周波数帯の割合の棒 1 本</summary>
public sealed record BandBar(string Label, double Share, string Percent, bool IsLargest);

/// <summary>1 つの条件の結果を、画面に出す形にしたもの</summary>
public sealed class ConditionCard
{
    public ConditionCard(TremorMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        Metrics = metrics;
        var ci = CultureInfo.InvariantCulture;
        Peak = metrics.PeakFrequencyHz.ToString("0.0", ci);
        Rms = metrics.RmsAccelerationMg < 10 ? metrics.RmsAccelerationMg.ToString("0.0", ci) : metrics.RmsAccelerationMg.ToString("0", ci);
        Displacement = metrics.EstimatedDisplacementMm.ToString(metrics.EstimatedDisplacementMm < 1 ? "0.00" : "0.0", ci);
        Regularity = (metrics.Regularity * 100).ToString("0", ci);
        double max = metrics.Bands.Count == 0 ? 0 : metrics.Bands.Max(b => b.Share);
        Bands = metrics.Bands.Select(b => new BandBar(b.Label, b.Share, (b.Share * 100).ToString("0", ci) + "%", b.Share == max && max > 0)).ToList();
    }

    public TremorMetrics Metrics { get; }

    public Condition Condition => Metrics.Condition;

    public bool IsRest => Condition == Condition.Rest;

    public string Title => Labels.Condition(Condition);

    public string Subtitle => Labels.ConditionPose(Condition);

    public string Peak { get; }

    public string Rms { get; }

    public string Displacement { get; }

    public string Regularity { get; }

    public IReadOnlyList<BandBar> Bands { get; }

    public QualityLevel Quality => Metrics.Quality.Level;

    public string QualityText => Labels.Quality(Quality);

    public IReadOnlyList<string> QualityMessages => Metrics.Quality.Messages;

    public bool HasQualityMessages => QualityMessages.Count > 0;

    public string Duration => $"{Metrics.DurationSeconds:0} 秒";
}

public static class Labels
{
    public static string Condition(Condition c) => c == Core.Domain.Condition.Rest ? "安静時" : "姿勢時";

    public static string ConditionPose(Condition c) => c == Core.Domain.Condition.Rest
        ? "腕を台の上に置き、力を抜いた状態"
        : "腕を前に伸ばし、その姿勢を保った状態";

    public static string Hand(Hand h) => h == Core.Domain.Hand.Right ? "右手" : "左手";

    public static string Quality(QualityLevel q) => q switch
    {
        QualityLevel.Good => "品質: 良好",
        QualityLevel.Caution => "品質: 注意",
        _ => "品質: 取り直し推奨",
    };

    public static string Pattern(TremorPattern p) => p switch
    {
        TremorPattern.RestDominant => "安静時に大きいふるえ",
        TremorPattern.PosturalDominant => "姿勢時に大きいふるえ",
        TremorPattern.Both => "安静時・姿勢時ともにふるえ",
        TremorPattern.NoClearTremor => "目立ったふるえは記録されず",
        _ => "比較できません",
    };

    public static string LocalTime(DateTime utc) => utc.ToLocalTime().ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);

    public const string Disclaimer =
        "この結果は加速度センサーで記録した「ふるえの物理的な特徴」です。病気の有無や種類を判断するものではありません。診断は医師が診察・ほかの検査とあわせて行ってください。";
}
