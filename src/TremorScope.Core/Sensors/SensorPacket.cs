using System.Text.Json;

namespace TremorScope.Core.Sensors;

/// <summary>センサーから届いた 1 回分の値（数十点ぶん）</summary>
public sealed record SensorPacket(double[][] Axes, string? DeviceId = null, long? Sequence = null, double? SampleRate = null, DateTimeOffset? ReceivedAt = null)
{
    public int Count => Axes.Length == 0 ? 0 : Axes[0].Length;
}

/// <summary>
/// センサーのメッセージ（JSON）を読む。2つの形式に対応する。
///
///   旧形式（試作版のセンサー）:  { "data": [0.012, -0.004, ...] }
///   新形式（推奨）:             { "v": 2, "device": "tremor-01", "seq": 1200, "fs": 50,
///                                 "ax": [...], "ay": [...], "az": [...] }
///   新形式・整数（通信量を減らす）:  上に "scale": 0.001 を加え、値を mg の整数で送る（値 × scale = g）
///
/// 新形式の seq は「最初の点の通し番号」で、途中のメッセージが届かなかったときに、何点欠けたかを数えるのに使う。
/// </summary>
public static class SensorMessageParser
{
    /// <summary>1 回のメッセージで受け付ける最大の点数（壊れた・悪意のあるメッセージへの備え）</summary>
    public const int MaxSamplesPerMessage = 2000;

    public static bool TryParse(ReadOnlySpan<byte> utf8Json, out SensorPacket? packet)
    {
        packet = null;
        try
        {
            using var doc = JsonDocument.Parse(utf8Json.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            if (root.TryGetProperty("ax", out var ax) && root.TryGetProperty("ay", out var ay) && root.TryGetProperty("az", out var az))
            {
                double scale = 1;
                if (root.TryGetProperty("scale", out var sc))
                {
                    if (sc.ValueKind != JsonValueKind.Number || !sc.TryGetDouble(out scale) || !double.IsFinite(scale) || scale <= 0 || scale > 1) return false;
                }
                var x = ReadArray(ax, scale); var y = ReadArray(ay, scale); var z = ReadArray(az, scale);
                if (x is null || y is null || z is null || x.Length != y.Length || x.Length != z.Length || x.Length == 0) return false;
                packet = new SensorPacket([x, y, z],
                    DeviceId: root.TryGetProperty("device", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null,
                    Sequence: root.TryGetProperty("seq", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt64(out long seq) && seq >= 0 ? seq : null,
                    SampleRate: root.TryGetProperty("fs", out var f) && f.ValueKind == JsonValueKind.Number && f.TryGetDouble(out double fs) && fs is > 0 and <= 2000 ? fs : null);
                return true;
            }

            if (root.TryGetProperty("data", out var data))
            {
                var values = ReadArray(data);
                if (values is null || values.Length == 0) return false;
                packet = new SensorPacket([values]);
                return true;
            }
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static double[]? ReadArray(JsonElement element, double scale = 1)
    {
        if (element.ValueKind != JsonValueKind.Array) return null;
        int length = element.GetArrayLength();
        if (length > MaxSamplesPerMessage) return null;
        var values = new double[length];
        int i = 0;
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetDouble(out double raw)) return null;
            double v = raw * scale;
            if (!double.IsFinite(v) || Math.Abs(v) > 64) return null;
            values[i++] = v;
        }
        return values;
    }
}
