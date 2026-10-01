using System.Globalization;
using System.Text;
using TremorScope.Core.Analysis;
using TremorScope.Core.Domain;

namespace TremorScope.Core.Export;

/// <summary>研究や院内の集計で使えるよう、結果と波形を CSV にする（表計算ソフトで開けるよう UTF-8 の BOM 付き）</summary>
public static class CsvExporter
{
    public static readonly UTF8Encoding Encoding = new(encoderShouldEmitUTF8Identifier: true);

    /// <summary>
    /// 測定結果の一覧。includeLocalId が false なら、カルテ番号を入れず仮名 ID だけにする（院外へ渡すとき用）
    /// </summary>
    public static string Sessions(IEnumerable<SessionSummary> sessions, bool includeLocalId)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        var sb = new StringBuilder();
        var header = new List<string> { "pseudonym_id" };
        if (includeLocalId) header.Add("local_id");
        header.AddRange(["measured_at_utc", "hand", "pattern",
            "rest_peak_hz", "rest_rms_mg", "rest_disp_mm", "rest_regularity", "rest_share_4_6", "rest_quality",
            "postural_peak_hz", "postural_rms_mg", "postural_disp_mm", "postural_regularity", "postural_share_4_6", "postural_quality",
            "algorithm_version"]);
        sb.AppendLine(string.Join(',', header));

        foreach (var s in sessions)
        {
            var row = new List<string> { Escape(s.PseudonymId) };
            if (includeLocalId) row.Add(Escape(s.LocalId));
            row.Add(s.MeasuredAtUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            row.Add(s.Hand.ToString());
            row.Add(s.Comparison.Pattern.ToString());
            row.AddRange(Metrics(s.Rest));
            row.AddRange(Metrics(s.Postural));
            row.Add(s.Rest?.AlgorithmVersion ?? s.Postural?.AlgorithmVersion ?? TremorAnalyzer.AlgorithmVersion);
            sb.AppendLine(string.Join(',', row));
        }
        return sb.ToString();
    }

    /// <summary>1 回の記録の波形（時刻と各軸の値）</summary>
    public static string Waveform(Recording recording)
    {
        ArgumentNullException.ThrowIfNull(recording);
        var sb = new StringBuilder();
        sb.AppendLine(recording.Axes.Length == 3 ? "time_s,ax_g,ay_g,az_g" : "time_s,value_g");
        for (int i = 0; i < recording.Length; i++)
        {
            sb.Append(F(i / recording.SampleRate, "0.000"));
            foreach (var axis in recording.Axes) sb.Append(',').Append(F(axis[i], "0.######"));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static IEnumerable<string> Metrics(TremorMetrics? m) => m is null
        ? Enumerable.Repeat("", 6)
        : [F(m.PeakFrequencyHz, "0.00"), F(m.RmsAccelerationMg, "0.00"), F(m.EstimatedDisplacementMm, "0.000"),
           F(m.Regularity, "0.000"), F(m.Share4To6, "0.000"), m.Quality.Level.ToString()];

    private static string F(double v, string format) => double.IsFinite(v) ? v.ToString(format, CultureInfo.InvariantCulture) : "";

    /// <summary>カンマや引用符を含む値を囲む。表計算ソフトで数式として動かないよう、= + - @ で始まる文字には ' を付ける</summary>
    internal static string Escape(string value)
    {
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0], StringComparison.Ordinal)) value = "'" + value;
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
    }
}
