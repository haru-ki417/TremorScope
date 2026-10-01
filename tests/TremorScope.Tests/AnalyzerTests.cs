using TremorScope.Core.Analysis;
using TremorScope.Core.Domain;
using TremorScope.Core.Sensors;

namespace TremorScope.Tests;

public class AnalyzerTests
{
    private static Recording OneAxis(double[] x, Condition c = Condition.Rest, int missing = 0) => new(c, 50, [x], missing);

    [Fact]
    public void 正弦波の周波数_加速度_変位を正しく求める()
    {
        // 5Hz・振幅 0.05g の正弦波 → RMS は 0.05/√2 g = 35.4 mg、変位の振幅は a/(2πf)² = 0.49 mm → RMS 0.35 mm
        var result = TremorAnalyzer.Analyze(OneAxis(Signals.Sine(5, 0.05, 20, offset: 0.98)));
        var m = result.Metrics;

        Assert.Equal(5.0, m.PeakFrequencyHz, 0.08);
        Assert.Equal(35.36, m.RmsAccelerationMg, 1.5);
        double expectedDisp = 0.05 * 9.80665 / Math.Pow(2 * Math.PI * 5, 2) / Math.Sqrt(2) * 1000;
        Assert.Equal(expectedDisp, m.EstimatedDisplacementMm, expectedDisp * 0.05);
        Assert.True(m.Regularity > 0.9);
        Assert.True(m.Share4To6 > 0.9);
        Assert.Equal(QualityLevel.Good, m.Quality.Level);
    }

    [Fact]
    public void 三軸ではセンサーの向きによらず同じ大きさになる()
    {
        var tremor = Signals.Sine(5, 0.05, 20);
        double c = Math.Cos(0.6), s = Math.Sin(0.6);
        // 同じふるえを、x 軸だけに出した場合と、斜めの向き（x と z に分かれる）に出した場合
        var straight = new Recording(Condition.Rest, 50, [tremor, new double[1000], Enumerable.Repeat(1.0, 1000).ToArray()]);
        var tilted = new Recording(Condition.Rest, 50, [tremor.Select(v => v * c).ToArray(), new double[1000], tremor.Select(v => 1.0 + v * s).ToArray()]);

        double a = TremorAnalyzer.Analyze(straight).Metrics.RmsAccelerationMg;
        double b = TremorAnalyzer.Analyze(tilted).Metrics.RmsAccelerationMg;
        Assert.Equal(a, b, a * 0.01);
    }

    [Fact]
    public void 主成分の向きの波形はふるえの形を保つ()
    {
        var tremor = Signals.Sine(6, 0.04, 20);
        var rec = new Recording(Condition.Rest, 50, [tremor.Select(v => v * 0.8).ToArray(), tremor.Select(v => v * 0.6).ToArray(), new double[1000]]);
        var dominant = TremorAnalyzer.Analyze(rec).FilteredSignal;
        double rms = Math.Sqrt(dominant.Skip(100).Take(800).Average(v => v * v));
        Assert.Equal(0.04 / Math.Sqrt(2), rms, 0.002);
    }

    [Fact]
    public void センサーが動いていなければ測り直しを求める()
    {
        var still = Enumerable.Repeat(0.98, 1000).Select((v, i) => v + (i % 2 == 0 ? 1e-6 : -1e-6)).ToArray();
        var m = TremorAnalyzer.Analyze(OneAxis(still)).Metrics;
        Assert.Equal(QualityLevel.Retake, m.Quality.Level);
        Assert.Contains(m.Quality.Messages, msg => msg.Contains("動いていません", StringComparison.Ordinal));
    }

    [Fact]
    public void データの欠けが多ければ測り直し_少しなら注意()
    {
        var x = Signals.Sine(5, 0.03, 20);
        Assert.Equal(QualityLevel.Retake, TremorAnalyzer.Analyze(OneAxis(x, missing: 100)).Metrics.Quality.Level);
        Assert.Equal(QualityLevel.Caution, TremorAnalyzer.Analyze(OneAxis(x, missing: 10)).Metrics.Quality.Level);
    }

    [Fact]
    public void 短すぎる測定は測り直し()
    {
        var m = TremorAnalyzer.Analyze(OneAxis(Signals.Sine(5, 0.03, 6))).Metrics;
        Assert.Equal(QualityLevel.Retake, m.Quality.Level);
    }

    [Fact]
    public void 値の振り切れを見つける()
    {
        // 振幅 1g のふるえを、±0.3g までしか測れないセンサーで測った場合（山が平らに切れる）
        var x = Signals.Sine(5, 1.0, 20).Select(v => Math.Clamp(v, -0.3, 0.3)).ToArray();
        var m = TremorAnalyzer.Analyze(OneAxis(x)).Metrics;
        Assert.Contains(m.Quality.Messages, msg => msg.Contains("測定範囲", StringComparison.Ordinal));
    }

    [Fact]
    public void 腕を大きく動かした測定には注意を出す()
    {
        var x = Signals.Add(Signals.Sine(0.5, 0.3, 20), Signals.Sine(5, 0.01, 20));
        var m = TremorAnalyzer.Analyze(OneAxis(x)).Metrics;
        Assert.Contains(m.Quality.Messages, msg => msg.Contains("大きく動かした", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(SimulationProfile.RestTremor, TremorPattern.RestDominant)]
    [InlineData(SimulationProfile.PosturalTremor, TremorPattern.PosturalDominant)]
    [InlineData(SimulationProfile.NoTremor, TremorPattern.NoClearTremor)]
    public void 見本の信号から期待どおりの傾向が出る(SimulationProfile profile, TremorPattern expected)
    {
        var rest = TremorAnalyzer.Analyze(SimulatedSource.CreateRecording(profile, Condition.Rest, 20)).Metrics;
        var postural = TremorAnalyzer.Analyze(SimulatedSource.CreateRecording(profile, Condition.Postural, 20)).Metrics;

        Assert.Equal(QualityLevel.Good, rest.Quality.Level);
        Assert.Equal(QualityLevel.Good, postural.Quality.Level);
        Assert.Equal(expected, ConditionComparer.Compare(rest, postural).Pattern);
    }

    [Fact]
    public void 見本の安静時振戦は5Hz付近()
    {
        var m = TremorAnalyzer.Analyze(SimulatedSource.CreateRecording(SimulationProfile.RestTremor, Condition.Rest, 20)).Metrics;
        Assert.InRange(m.PeakFrequencyHz, 4.6, 5.4);
        Assert.True(m.Share4To6 > 0.7);
    }

    [Fact]
    public void 品質に問題があれば比較しない()
    {
        var good = TremorAnalyzer.Analyze(OneAxis(Signals.Sine(5, 0.05, 20))).Metrics;
        var bad = TremorAnalyzer.Analyze(OneAxis(Signals.Sine(5, 0.05, 20), Condition.Postural, missing: 200)).Metrics;
        Assert.Equal(TremorPattern.Undetermined, ConditionComparer.Compare(good, bad).Pattern);
        Assert.Equal(TremorPattern.Undetermined, ConditionComparer.Compare(good, null).Pattern);
    }
}
