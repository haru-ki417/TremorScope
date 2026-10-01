namespace TremorScope.Tests;

/// <summary>テスト用の信号を作る</summary>
internal static class Signals
{
    public static double[] Sine(double frequencyHz, double amplitude, double seconds, double sampleRate = 50, double offset = 0, double phase = 0) =>
        Enumerable.Range(0, (int)(seconds * sampleRate))
            .Select(i => offset + amplitude * Math.Sin(2 * Math.PI * frequencyHz * i / sampleRate + phase))
            .ToArray();

    public static double[] Noise(int length, double amplitude, int seed = 3)
    {
        var random = new Random(seed);
        return Enumerable.Range(0, length).Select(_ => (random.NextDouble() - 0.5) * 2 * amplitude).ToArray();
    }

    public static double[] Add(double[] a, double[] b) => a.Zip(b, (x, y) => x + y).ToArray();
}
