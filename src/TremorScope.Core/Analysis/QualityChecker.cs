using TremorScope.Core.Domain;
using TremorScope.Core.Signal;

namespace TremorScope.Core.Analysis;

/// <summary>
/// 測定がうまくいったかを確かめる。失敗した測定を「ふるえが無い」と誤って記録しないため。
/// </summary>
public static class QualityChecker
{
    /// <summary>この大きさ（mg, RMS）より小さければ、センサーを持っていないとみなす（机の上に置いたままなど）</summary>
    public const double StillThresholdMg = 0.3;

    /// <summary>届かなかった点の割合がこれを超えたら測り直し</summary>
    public const double MaxMissingRatio = 0.05;

    public static QualityReport Check(Recording recording, double[][] highPassed, Spectrum spectrum)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(highPassed);
        ArgumentNullException.ThrowIfNull(spectrum);

        var level = QualityLevel.Good;
        var messages = new List<string>();
        void Add(QualityLevel l, string message)
        {
            if (l > level) level = l;
            messages.Add(message);
        }

        // 1. センサーが動いていない（すべての帯域で、ほぼ 0）
        double broadband = Math.Sqrt(spectrum.BandPower(1, recording.SampleRate / 2)) * 1000;
        if (broadband < StillThresholdMg)
            Add(QualityLevel.Retake, "センサーがほとんど動いていません。センサーを手に持っているか確認してください。");

        // 2. データの途切れ
        int expected = recording.Length + recording.MissingSamples;
        double missingRatio = expected == 0 ? 0 : (double)recording.MissingSamples / expected;
        if (missingRatio > MaxMissingRatio)
            Add(QualityLevel.Retake, $"通信の途切れで {missingRatio:P0} のデータが届きませんでした。電波の状態を確認して測り直してください。");
        else if (recording.MissingSamples > 0)
            Add(QualityLevel.Caution, $"{recording.MissingSamples} 点のデータが届きませんでした（{missingRatio:P1}）。");

        // 3. 長さが足りない
        if (recording.DurationSeconds < 10)
            Add(QualityLevel.Retake, $"測定時間が短すぎます（{recording.DurationSeconds:0.0} 秒）。10 秒以上必要です。");

        // 4. 値の振り切れ（センサーの測定範囲を超えた）
        if (HasClipping(recording.Axes))
            Add(QualityLevel.Caution, "センサーの測定範囲を超えた値があります。大きな動きや衝撃がなかったか確認してください。");

        // 5. 大きな動きの混入（1Hz 未満の動きが、ふるえの帯域より極端に大きい）
        double slow = SlowMovementPowerMg(recording);
        double tremor = Math.Sqrt(spectrum.BandPower(TremorAnalyzer.TremorBandLowHz, TremorAnalyzer.TremorBandHighHz)) * 1000;
        if (slow > 50 && slow > 4 * tremor)
            Add(QualityLevel.Caution, "測定中に腕を大きく動かした可能性があります。姿勢を保ったまま測り直すと、より正確になります。");

        return messages.Count == 0 ? QualityReport.Ok : new QualityReport(level, messages);
    }

    /// <summary>
    /// 最大値（または最小値）とまったく同じ値が 3 点以上続く箇所があり、その値の点が全体の 1% 以上あれば、
    /// センサーの測定範囲を超えて値が頭打ちになったとみなす
    /// </summary>
    internal static bool HasClipping(double[][] axes)
    {
        foreach (var axis in axes)
        {
            double max = axis.Max(), min = axis.Min();
            if (max - min < 1e-9) continue; // まったく動いていないのは別の確認で扱う
            foreach (double extreme in new[] { max, min })
            {
                int run = 0, longest = 0, count = 0;
                foreach (double v in axis)
                {
                    bool same = Math.Abs(v - extreme) < 1e-12;
                    if (same) count++;
                    run = same ? run + 1 : 0;
                    longest = Math.Max(longest, run);
                }
                if (longest >= 3 && count >= axis.Length / 100.0) return true;
            }
        }
        return false;
    }

    /// <summary>0.2〜1Hz の動き（腕を動かした、姿勢を変えた）の大きさ（mg）</summary>
    private static double SlowMovementPowerMg(Recording recording)
    {
        double fs = recording.SampleRate;
        if (recording.Length < 128) return 0;
        var lowPass = Biquad.LowPass(1.0, fs);
        double sum = 0;
        foreach (var axis in recording.Axes)
        {
            var slow = lowPass.FiltFilt(TremorAnalyzer.RemoveMean(axis));
            // 端の影響を避けて中央の部分だけを見る
            int skip = (int)fs;
            var middle = slow.AsSpan(skip, slow.Length - 2 * skip);
            double ss = 0;
            foreach (double v in middle) ss += v * v;
            sum += ss / middle.Length;
        }
        return Math.Sqrt(sum) * 1000;
    }
}
