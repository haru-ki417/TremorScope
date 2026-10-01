using TremorScope.Core.Analysis;

namespace TremorScope.Core.Domain;

/// <summary>1 回の受診での測定（安静時・姿勢時の 2 つの記録と、その比較）</summary>
public sealed record SessionSummary(
    Guid Id,
    Guid PatientId,
    string LocalId,
    string PseudonymId,
    DateTime MeasuredAtUtc,
    Hand Hand,
    TremorMetrics? Rest,
    TremorMetrics? Postural,
    ConditionComparison Comparison,
    string? Note);
