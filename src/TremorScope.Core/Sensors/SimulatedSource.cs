using System.Runtime.CompilerServices;
using TremorScope.Core.Domain;

namespace TremorScope.Core.Sensors;

/// <summary>見本の信号の種類（センサーが無くても画面を試せるようにするため）</summary>
public enum SimulationProfile
{
    /// <summary>安静時に 4〜6Hz の規則的なふるえ、姿勢をとると小さくなる</summary>
    RestTremor = 0,

    /// <summary>姿勢時に 6〜8Hz のふるえ、安静時は小さい</summary>
    PosturalTremor = 1,

    /// <summary>目立ったふるえなし（生理的な小さなふるえだけ）</summary>
    NoTremor = 2,
}

/// <summary>
/// 3軸加速度センサー（50Hz）を真似た見本の信号。
/// 重力（約 1g）・生理的な小さなふるえ・ゆっくりした揺れ・センサーの雑音を重ね、
/// ふるえの周波数と大きさも少しずつ揺らして、実際の測定に近づけている。
/// </summary>
public sealed class SimulatedSource(SimulationProfile profile, Func<Condition> currentCondition, int seed = 0) : ISampleSource
{
    public const double SampleRate = 50;
    private const int PacketSize = 25; // 0.5 秒ごとに送る

    public string Name => profile switch
    {
        SimulationProfile.RestTremor => "見本: 安静時に目立つふるえ",
        SimulationProfile.PosturalTremor => "見本: 姿勢時に目立つふるえ",
        _ => "見本: 目立ったふるえなし",
    };

    public async IAsyncEnumerable<SensorPacket> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var random = seed == 0 ? new Random() : new Random(seed);
        long sequence = 0;
        double phase = 0, t = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(PacketSize / SampleRate));
        while (!cancellationToken.IsCancellationRequested)
        {
            yield return Generate(random, currentCondition(), ref sequence, ref phase, ref t);
            try
            {
                if (!await timer.WaitForNextTickAsync(cancellationToken)) yield break;
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
        }
    }

    /// <summary>指定した長さの記録を、待たずに作る（テストや画面の見本用）</summary>
    public static Recording CreateRecording(SimulationProfile profile, Condition condition, double seconds, int seed = 1)
    {
        var source = new SimulatedSource(profile, () => condition, seed);
        var random = new Random(seed);
        long sequence = 0;
        double phase = 0, t = 0;
        int total = (int)(seconds * SampleRate);
        var axes = new[] { new List<double>(), new List<double>(), new List<double>() };
        while (axes[0].Count < total)
        {
            var packet = source.Generate(random, condition, ref sequence, ref phase, ref t);
            for (int a = 0; a < 3; a++) axes[a].AddRange(packet.Axes[a]);
        }
        return new Recording(condition, SampleRate, axes.Select(a => a.Take(total).ToArray()).ToArray(), deviceId: "simulator");
    }

    private (double FrequencyHz, double AmplitudeG) TremorFor(Condition condition) => (profile, condition) switch
    {
        (SimulationProfile.RestTremor, Condition.Rest) => (5.0, 0.060),
        (SimulationProfile.RestTremor, Condition.Postural) => (5.3, 0.014),
        (SimulationProfile.PosturalTremor, Condition.Rest) => (6.8, 0.006),
        (SimulationProfile.PosturalTremor, Condition.Postural) => (7.2, 0.045),
        _ => (9.5, 0.0025), // 生理的振戦（8〜12Hz、ごく小さい）
    };

    private SensorPacket Generate(Random random, Condition condition, ref long sequence, ref double phase, ref double t)
    {
        var (freq, amp) = TremorFor(condition);
        var x = new double[PacketSize]; var y = new double[PacketSize]; var z = new double[PacketSize];
        for (int i = 0; i < PacketSize; i++)
        {
            // 周波数と大きさを、数秒の周期でゆっくり揺らす
            double f = freq * (1 + 0.04 * Math.Sin(2 * Math.PI * 0.13 * t));
            double a = amp * (1 + 0.25 * Math.Sin(2 * Math.PI * 0.21 * t + 1.3));
            phase += 2 * Math.PI * f / SampleRate;
            double tremor = a * Math.Sin(phase);
            double physiological = 0.0015 * Math.Sin(2 * Math.PI * 10.2 * t + 0.7);
            double sway = 0.004 * Math.Sin(2 * Math.PI * 0.3 * t);

            // ふるえの向きを主に x 方向、少し y 方向にも出す。z には重力（1g）がかかる
            x[i] = 0.08 + tremor * 0.85 + physiological + sway + Noise(random);
            y[i] = -0.12 + tremor * 0.45 + physiological * 0.5 + Noise(random);
            z[i] = 0.985 + tremor * 0.25 + sway * 0.5 + Noise(random);
            t += 1 / SampleRate;
        }
        var packet = new SensorPacket([x, y, z], "simulator", sequence, SampleRate, DateTimeOffset.UtcNow);
        sequence += PacketSize;
        return packet;
    }

    private static double Noise(Random random) => (random.NextDouble() - 0.5) * 0.002;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
