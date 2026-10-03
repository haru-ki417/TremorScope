using System.Runtime.InteropServices;
using TremorScope.Core.Analysis;
using TremorScope.Core.Domain;

namespace TremorScope.Web.State;

/// <summary>1 回の測定（安静時・姿勢時の記録と、その解析・比較）</summary>
public sealed class Session
{
    public Session(Guid id, string label, Hand hand, string note, DateTime measuredAtUtc, double thresholdMg, string source, IEnumerable<Recording> recordings)
    {
        Id = id;
        Label = label;
        Hand = hand;
        Note = note;
        MeasuredAtUtc = measuredAtUtc;
        ThresholdMg = thresholdMg;
        Source = source;
        foreach (var r in recordings)
        {
            var result = TremorAnalyzer.Analyze(r);
            if (r.Condition == Condition.Rest)
            {
                Rest = r;
                RestResult = result;
            }
            else
            {
                Postural = r;
                PosturalResult = result;
            }
        }
        Comparison = ConditionComparer.Compare(RestResult?.Metrics, PosturalResult?.Metrics, thresholdMg);
    }

    public Guid Id { get; }

    /// <summary>名前・ID（任意）。この端末の中だけに保存する</summary>
    public string Label { get; }

    public Hand Hand { get; }

    public string Note { get; }

    public DateTime MeasuredAtUtc { get; }

    public double ThresholdMg { get; }

    /// <summary>センサーの種類（「このスマホ」「見本の信号」など）</summary>
    public string Source { get; }

    public Recording? Rest { get; }

    public Recording? Postural { get; }

    public AnalysisResult? RestResult { get; }

    public AnalysisResult? PosturalResult { get; }

    public ConditionComparison Comparison { get; }

    public AnalysisResult? ResultOf(Condition c) => c == Condition.Rest ? RestResult : PosturalResult;

    public Recording? RecordingOf(Condition c) => c == Condition.Rest ? Rest : Postural;

    public string DisplayLabel => string.IsNullOrWhiteSpace(Label) ? "（名前なし）" : Label;

    public SessionSummary ToSummary() =>
        new(Id, Guid.Empty, Label, "", MeasuredAtUtc, Hand, RestResult?.Metrics, PosturalResult?.Metrics, Comparison, Note);

    // ---- この端末への保存（波形は 32 bit の小数を base64 にして小さくする。結果は読み込むときに計算し直す）

    public StoredSession ToStored() => new(Id, Label, (int)Hand, Note, MeasuredAtUtc, ThresholdMg, Source,
        new[] { Rest, Postural }.Where(r => r is not null).Select(r => new StoredRecording(
            (int)r!.Condition, r.SampleRate, r.MissingSamples, r.StartedAtUtc, r.Axes.Select(Encode).ToList())).ToList());

    public static Session FromStored(StoredSession s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var recordings = s.Recordings.Select(r => new Recording((Condition)r.Condition, r.SampleRate,
            r.Axes.Select(Decode).ToArray(), r.Missing, r.StartedAtUtc, null));
        return new Session(s.Id, s.Label, (Hand)s.Hand, s.Note, s.MeasuredAtUtc, s.ThresholdMg, s.Source, recordings);
    }

    private static string Encode(double[] values)
    {
        var f = values.Select(v => (float)v).ToArray();
        return Convert.ToBase64String(MemoryMarshal.AsBytes(f.AsSpan()));
    }

    private static double[] Decode(string base64)
    {
        var bytes = Convert.FromBase64String(base64);
        return MemoryMarshal.Cast<byte, float>(bytes.AsSpan()).ToArray().Select(v => (double)v).ToArray();
    }
}

public sealed record StoredRecording(int Condition, double SampleRate, int Missing, DateTime StartedAtUtc, List<string> Axes);

public sealed record StoredSession(Guid Id, string Label, int Hand, string Note, DateTime MeasuredAtUtc, double ThresholdMg, string Source, List<StoredRecording> Recordings);

public sealed record StoredSettings(int MeasurementSeconds, double ThresholdMg, int PrepareSeconds);
