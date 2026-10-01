using TremorScope.Core.Signal;

namespace TremorScope.Tests;

public class SignalTests
{
    [Fact]
    public void 高域通過フィルターは重力のような一定の値とゆっくりした揺れを取り除く()
    {
        var slow = Signals.Sine(0.2, 0.05, 20, offset: 1.0);   // 重力 + 0.2Hz の揺れ
        var fast = Signals.Sine(5, 0.02, 20);                   // 5Hz のふるえ
        var filtered = Biquad.HighPass(1, 50).FiltFilt(Signals.Add(slow, fast));

        // 端を除いた中央部分で、5Hz の成分はほぼそのまま、ほかは消えている
        var middle = filtered.Skip(100).Take(800).ToArray();
        var expected = fast.Skip(100).Take(800).ToArray();
        double error = Math.Sqrt(middle.Zip(expected, (a, b) => (a - b) * (a - b)).Average());
        Assert.True(error < 0.002, $"誤差 {error}");
    }

    [Fact]
    public void 前後2回かけるので波形の位置がずれない()
    {
        var x = Signals.Sine(6, 0.03, 10);
        var y = Biquad.HighPass(1, 50).FiltFilt(x);
        // 山の位置が同じ（相互相関が遅れ 0 で最大）
        double Corr(int lag) => Enumerable.Range(100, 300).Sum(i => x[i] * y[i + lag]);
        Assert.True(Corr(0) > Corr(1) && Corr(0) > Corr(-1));
    }

    [Theory]
    [InlineData(4.3)]
    [InlineData(5.0)]
    [InlineData(7.7)]
    [InlineData(11.2)]
    public void スペクトルの山は正しい周波数に出る(double frequency)
    {
        var spectrum = Spectrum.Welch(Signals.Sine(frequency, 0.05, 20), 50);
        var (peak, _) = spectrum.Peak(2, 15);
        Assert.Equal(frequency, peak, 0.08); // 分解能 0.2Hz を補間で細かくしている
    }

    [Fact]
    public void スペクトルの合計は信号の分散に等しい_パーセバルの定理()
    {
        var x = Signals.Add(Signals.Sine(5, 0.04, 20), Signals.Noise(1000, 0.01));
        double mean = x.Average();
        double variance = x.Sum(v => (v - mean) * (v - mean)) / x.Length;

        var spectrum = Spectrum.Welch(x, 50);
        double total = spectrum.BandPower(0, 25);
        Assert.Equal(variance, total, variance * 0.08);
    }

    [Fact]
    public void 正弦波の強さは振幅の2乗の半分()
    {
        const double amplitude = 0.05;
        var spectrum = Spectrum.Welch(Signals.Sine(6, amplitude, 20), 50);
        Assert.Equal(amplitude * amplitude / 2, spectrum.BandPower(4, 8), 1e-5);
    }
}
