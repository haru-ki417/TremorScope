using System.ComponentModel.DataAnnotations;
using TremorScope.Core.Analysis;
using TremorScope.Core.Domain;

namespace TremorScope.Infrastructure.Storage;

/// <summary>
/// 患者。カルテ番号はこの PC の中だけに保存し、クラウドや院外には仮名 ID だけを出す。
/// 氏名・生年月日は扱わない（測定と経過の確認に必要ないため）。
/// </summary>
public sealed class PatientEntity
{
    public Guid Id { get; set; }

    [MaxLength(64)]
    public required string LocalId { get; set; }

    [MaxLength(16)]
    public required string PseudonymId { get; set; }

    [MaxLength(200)]
    public string? Note { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public List<SessionEntity> Sessions { get; set; } = [];
}

/// <summary>1 回の受診での測定</summary>
public sealed class SessionEntity
{
    public Guid Id { get; set; }

    public Guid PatientId { get; set; }

    public PatientEntity? Patient { get; set; }

    public DateTime MeasuredAtUtc { get; set; }

    public Hand Hand { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    public TremorPattern Pattern { get; set; }

    [MaxLength(300)]
    public string Summary { get; set; } = "";

    [MaxLength(32)]
    public string AppVersion { get; set; } = "";

    /// <summary>クラウドに保存した時刻（まだなら null）</summary>
    public DateTime? CloudSyncedAtUtc { get; set; }

    /// <summary>PACS に送った時刻（まだなら null）</summary>
    public DateTime? PacsSentAtUtc { get; set; }

    public List<RecordingEntity> Recordings { get; set; } = [];
}

/// <summary>1 つの条件（安静時・姿勢時）の記録と解析結果</summary>
public sealed class RecordingEntity
{
    public Guid Id { get; set; }

    public Guid SessionId { get; set; }

    public SessionEntity? Session { get; set; }

    public Condition Condition { get; set; }

    public double SampleRate { get; set; }

    public int AxisCount { get; set; }

    public int SampleCount { get; set; }

    public int MissingSamples { get; set; }

    [MaxLength(64)]
    public string? DeviceId { get; set; }

    public DateTime StartedAtUtc { get; set; }

    /// <summary>波形（float32、軸ごとに続けて並べる）</summary>
    public byte[] Samples { get; set; } = [];

    // ---- 解析結果
    public double PeakFrequencyHz { get; set; }

    public double RmsAccelerationMg { get; set; }

    public double EstimatedDisplacementMm { get; set; }

    public double Regularity { get; set; }

    /// <summary>帯域ごとの割合（JSON）</summary>
    public string BandsJson { get; set; } = "[]";

    public QualityLevel QualityLevel { get; set; }

    /// <summary>品質の注意（JSON の文字列配列）</summary>
    public string QualityMessagesJson { get; set; } = "[]";

    [MaxLength(16)]
    public string AlgorithmVersion { get; set; } = "";
}
