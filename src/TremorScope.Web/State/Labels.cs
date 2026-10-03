using System.Globalization;
using TremorScope.Core.Analysis;
using TremorScope.Core.Domain;

namespace TremorScope.Web.State;

/// <summary>画面に出す言葉（Windows 版と同じ）</summary>
public static class Labels
{
    public static string Condition(Condition c) => c == Core.Domain.Condition.Rest ? "安静時" : "姿勢時";

    public static string ConditionPose(Condition c) => c == Core.Domain.Condition.Rest
        ? "腕を台の上に置き、力を抜いた状態"
        : "腕を前に伸ばし、その姿勢を保った状態";

    public static string ConditionKey(Condition c) => c == Core.Domain.Condition.Rest ? "rest" : "postural";

    public static string Hand(Hand h) => h == Core.Domain.Hand.Right ? "右手" : "左手";

    public static string Quality(QualityLevel q) => q switch
    {
        QualityLevel.Good => "品質: 良好",
        QualityLevel.Caution => "品質: 注意",
        _ => "品質: 取り直し推奨",
    };

    public static string QualityKey(QualityLevel q) => q switch
    {
        QualityLevel.Good => "good",
        QualityLevel.Caution => "caution",
        _ => "retake",
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

    public static string F(double v, string format = "0.#") => v.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>大きさに応じて桁を変える（36 mg、8.5 mg、0.36 mm）</summary>
    public static string Auto(double v) => Math.Abs(v) >= 10 ? F(v, "0") : Math.Abs(v) >= 1 ? F(v, "0.0") : F(v, "0.00");

    public const string Disclaimer =
        "この結果は加速度センサーで記録した「ふるえの物理的な特徴」です。病気の有無や種類を判断するものではありません。診断は医師が診察・ほかの検査とあわせて行ってください。";
}
