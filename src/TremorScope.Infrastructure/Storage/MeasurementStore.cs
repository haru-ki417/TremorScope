using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TremorScope.Core.Analysis;
using TremorScope.Core.Domain;
using TremorScope.Core.Privacy;

namespace TremorScope.Infrastructure.Storage;

/// <summary>保存した測定（波形と解析結果）</summary>
public sealed record StoredRecording(Recording Recording, TremorMetrics Metrics);

/// <summary>患者の一覧に出す内容</summary>
public sealed record PatientListItem(Guid Id, string LocalId, string PseudonymId, string? Note, int SessionCount, DateTime? LastMeasuredAtUtc);

/// <summary>
/// 患者と測定を、この PC の中のデータベース（SQLite）に保存する。
/// 1 つの操作ごとに新しい接続を使う（画面から同時に呼ばれても安全なように）。
/// </summary>
public sealed class MeasurementStore(Func<TremorDbContext> createContext, Pseudonymizer pseudonymizer)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var db = createContext();
        await db.Database.EnsureCreatedAsync(cancellationToken);
    }

    // ---- 患者

    public async Task<IReadOnlyList<PatientListItem>> ListPatientsAsync(string? search = null, CancellationToken cancellationToken = default)
    {
        await using var db = createContext();
        var query = db.Patients.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            string s = Pseudonymizer.Normalize(search);
            query = query.Where(p => p.LocalId.Contains(s) || p.PseudonymId.Contains(s) || (p.Note != null && p.Note.Contains(search.Trim())));
        }
        var list = await query
            .Select(p => new PatientListItem(p.Id, p.LocalId, p.PseudonymId, p.Note, p.Sessions.Count, p.Sessions.Max(s => (DateTime?)s.MeasuredAtUtc)))
            .ToListAsync(cancellationToken);
        return list.OrderByDescending(p => p.LastMeasuredAtUtc ?? DateTime.MinValue).ThenBy(p => p.LocalId, StringComparer.Ordinal).ToList();
    }

    /// <summary>カルテ番号で患者を探し、いなければ登録する</summary>
    public async Task<PatientListItem> GetOrCreatePatientAsync(string localId, string? note = null, CancellationToken cancellationToken = default)
    {
        string normalized = Pseudonymizer.Normalize(localId);
        if (normalized.Length is 0 or > 64) throw new ArgumentException("カルテ番号は 1〜64 文字で入力してください。", nameof(localId));

        await using var db = createContext();
        var patient = await db.Patients.FirstOrDefaultAsync(p => p.LocalId == normalized, cancellationToken);
        if (patient is null)
        {
            patient = new PatientEntity
            {
                Id = Guid.NewGuid(),
                LocalId = normalized,
                PseudonymId = pseudonymizer.Pseudonymize(normalized),
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
                CreatedAtUtc = DateTime.UtcNow,
            };
            db.Patients.Add(patient);
            await db.SaveChangesAsync(cancellationToken);
        }
        int count = await db.Sessions.CountAsync(s => s.PatientId == patient.Id, cancellationToken);
        var last = await db.Sessions.Where(s => s.PatientId == patient.Id).MaxAsync(s => (DateTime?)s.MeasuredAtUtc, cancellationToken);
        return new PatientListItem(patient.Id, patient.LocalId, patient.PseudonymId, patient.Note, count, last);
    }

    /// <summary>患者と、その測定をすべて削除する（元に戻せない）。クラウドから消すための仮名 ID を返す</summary>
    public async Task<string?> DeletePatientAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        await using var db = createContext();
        var patient = await db.Patients.FirstOrDefaultAsync(p => p.Id == patientId, cancellationToken);
        if (patient is null) return null;
        db.Patients.Remove(patient);
        await db.SaveChangesAsync(cancellationToken);
        return patient.PseudonymId;
    }

    // ---- 測定

    public async Task<SessionSummary> SaveSessionAsync(Guid patientId, Hand hand, string? note, IReadOnlyList<StoredRecording> recordings, string appVersion,
        DateTime? measuredAtUtc = null, double thresholdMg = 10, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recordings);
        if (recordings.Count == 0) throw new ArgumentException("記録がありません。", nameof(recordings));

        await using var db = createContext();
        var patient = await db.Patients.FirstOrDefaultAsync(p => p.Id == patientId, cancellationToken)
            ?? throw new InvalidOperationException("患者が見つかりません。");

        var rest = recordings.FirstOrDefault(r => r.Recording.Condition == Condition.Rest)?.Metrics;
        var postural = recordings.FirstOrDefault(r => r.Recording.Condition == Condition.Postural)?.Metrics;
        var comparison = ConditionComparer.Compare(rest, postural, thresholdMg);

        var session = new SessionEntity
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            MeasuredAtUtc = measuredAtUtc ?? DateTime.UtcNow,
            Hand = hand,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            Pattern = comparison.Pattern,
            Summary = comparison.Summary,
            AppVersion = appVersion,
            Recordings = recordings.Select(r => ToEntity(r.Recording, r.Metrics)).ToList(),
        };
        db.Sessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        return ToSummary(session, patient);
    }

    public async Task<IReadOnlyList<SessionSummary>> ListSessionsAsync(Guid? patientId = null, CancellationToken cancellationToken = default)
    {
        await using var db = createContext();
        var query = db.Sessions.AsNoTracking().Include(s => s.Patient).Include(s => s.Recordings).AsQueryable();
        if (patientId is Guid id) query = query.Where(s => s.PatientId == id);
        var sessions = await query.ToListAsync(cancellationToken);
        return sessions.OrderByDescending(s => s.MeasuredAtUtc).Select(s => ToSummary(s, s.Patient!)).ToList();
    }

    public async Task<(SessionSummary Summary, IReadOnlyList<Recording> Recordings)?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await using var db = createContext();
        var s = await db.Sessions.AsNoTracking().Include(x => x.Patient).Include(x => x.Recordings)
            .FirstOrDefaultAsync(x => x.Id == sessionId, cancellationToken);
        if (s is null) return null;
        return (ToSummary(s, s.Patient!), s.Recordings.OrderBy(r => r.Condition).Select(ToRecording).ToList());
    }

    /// <summary>まだクラウドに保存していない測定</summary>
    public async Task<IReadOnlyList<Guid>> PendingCloudSyncAsync(CancellationToken cancellationToken = default)
    {
        await using var db = createContext();
        return await db.Sessions.Where(s => s.CloudSyncedAtUtc == null).OrderBy(s => s.MeasuredAtUtc).Select(s => s.Id).ToListAsync(cancellationToken);
    }

    public async Task MarkCloudSyncedAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await using var db = createContext();
        await db.Sessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.CloudSyncedAtUtc, DateTime.UtcNow), cancellationToken);
    }

    public async Task MarkPacsSentAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await using var db = createContext();
        await db.Sessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.PacsSentAtUtc, DateTime.UtcNow), cancellationToken);
    }

    public async Task<(DateTime? Cloud, DateTime? Pacs)> GetDeliveryStatusAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await using var db = createContext();
        var s = await db.Sessions.AsNoTracking().Where(x => x.Id == sessionId).Select(x => new { x.CloudSyncedAtUtc, x.PacsSentAtUtc }).FirstOrDefaultAsync(cancellationToken);
        return (s?.CloudSyncedAtUtc, s?.PacsSentAtUtc);
    }

    // ---- 変換

    private static RecordingEntity ToEntity(Recording r, TremorMetrics m) => new()
    {
        Id = Guid.NewGuid(),
        Condition = r.Condition,
        SampleRate = r.SampleRate,
        AxisCount = r.Axes.Length,
        SampleCount = r.Length,
        MissingSamples = r.MissingSamples,
        DeviceId = r.DeviceId,
        StartedAtUtc = r.StartedAtUtc,
        Samples = Pack(r.Axes),
        PeakFrequencyHz = double.IsFinite(m.PeakFrequencyHz) ? m.PeakFrequencyHz : 0,
        RmsAccelerationMg = m.RmsAccelerationMg,
        EstimatedDisplacementMm = m.EstimatedDisplacementMm,
        Regularity = m.Regularity,
        BandsJson = JsonSerializer.Serialize(m.Bands),
        QualityLevel = m.Quality.Level,
        QualityMessagesJson = JsonSerializer.Serialize(m.Quality.Messages),
        AlgorithmVersion = m.AlgorithmVersion,
    };

    internal static TremorMetrics ToMetrics(RecordingEntity e) => new(
        e.Condition, e.PeakFrequencyHz, e.RmsAccelerationMg, e.EstimatedDisplacementMm, e.Regularity,
        JsonSerializer.Deserialize<List<BandShare>>(e.BandsJson) ?? [],
        new QualityReport(e.QualityLevel, JsonSerializer.Deserialize<List<string>>(e.QualityMessagesJson) ?? []),
        e.SampleRate > 0 ? e.SampleCount / e.SampleRate : 0,
        e.AlgorithmVersion);

    private static Recording ToRecording(RecordingEntity e) =>
        new(e.Condition, e.SampleRate, Unpack(e.Samples, e.AxisCount, e.SampleCount), e.MissingSamples, DateTime.SpecifyKind(e.StartedAtUtc, DateTimeKind.Utc), e.DeviceId);

    private static SessionSummary ToSummary(SessionEntity s, PatientEntity p)
    {
        var rest = s.Recordings.FirstOrDefault(r => r.Condition == Condition.Rest) is { } r1 ? ToMetrics(r1) : null;
        var postural = s.Recordings.FirstOrDefault(r => r.Condition == Condition.Postural) is { } r2 ? ToMetrics(r2) : null;
        return new SessionSummary(s.Id, p.Id, p.LocalId, p.PseudonymId, DateTime.SpecifyKind(s.MeasuredAtUtc, DateTimeKind.Utc), s.Hand,
            rest, postural, new ConditionComparison(s.Pattern, rest is not null && postural is not null && postural.RmsAccelerationMg > 0 ? rest.RmsAccelerationMg / postural.RmsAccelerationMg : null, s.Summary), s.Note);
    }

    internal static byte[] Pack(double[][] axes)
    {
        var floats = axes.SelectMany(a => a.Select(v => (float)v)).ToArray();
        return MemoryMarshal.AsBytes(floats.AsSpan()).ToArray();
    }

    internal static double[][] Unpack(byte[] bytes, int axisCount, int length)
    {
        var floats = MemoryMarshal.Cast<byte, float>(bytes.AsSpan());
        var axes = new double[axisCount][];
        for (int a = 0; a < axisCount; a++)
        {
            axes[a] = new double[length];
            var slice = floats.Slice(a * length, length);
            for (int i = 0; i < length; i++) axes[a][i] = slice[i];
        }
        return axes;
    }
}
