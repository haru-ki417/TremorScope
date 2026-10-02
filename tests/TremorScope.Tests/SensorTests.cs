using System.Text;
using TremorScope.Core.Domain;
using TremorScope.Core.Sensors;

namespace TremorScope.Tests;

public class SensorTests
{
    private static SensorPacket? Parse(string json) => SensorMessageParser.TryParse(Encoding.UTF8.GetBytes(json), out var p) ? p : null;

    [Fact]
    public void 試作版の形式を読める()
    {
        var p = Parse("""{"data":[0.01,-0.02,0.03]}""");
        Assert.NotNull(p);
        Assert.Single(p.Axes);
        Assert.Equal([0.01, -0.02, 0.03], p.Axes[0]);
        Assert.Null(p.Sequence);
    }

    [Fact]
    public void 新しい形式を読める()
    {
        var p = Parse("""{"v":2,"device":"tremor-01","seq":100,"fs":50,"ax":[0.1,0.2],"ay":[0,0],"az":[1,1]}""");
        Assert.NotNull(p);
        Assert.Equal(3, p.Axes.Length);
        Assert.Equal(("tremor-01", 100L, 50.0), (p.DeviceId!, p.Sequence!.Value, p.SampleRate!.Value));
    }

    [Fact]
    public void 整数_mg_で送られた値を_g_に直して読む()
    {
        var p = Parse("""{"v":2,"seq":0,"fs":50,"scale":0.001,"ax":[12,-3],"ay":[0,5],"az":[998,1003]}""");
        Assert.NotNull(p);
        Assert.Equal(0.012, p.Axes[0][0], 1e-12);
        Assert.Equal(1.003, p.Axes[2][1], 1e-12);
    }

    [Fact]
    public void 通し番号やサンプリング周波数が数値でなければ_その項目だけ無視する()
    {
        var p = Parse("""{"seq":"x","fs":"fast","ax":[0.1],"ay":[0],"az":[1]}""");
        Assert.NotNull(p);
        Assert.Null(p.Sequence);
        Assert.Null(p.SampleRate);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"scale":0,"ax":[1],"ay":[1],"az":[1]}""")]
    [InlineData("""{"scale":"x","ax":[1],"ay":[1],"az":[1]}""")]
    [InlineData("""{"scale":0.001,"ax":[99999],"ay":[1],"az":[1]}""")]
    [InlineData("""{"data":"abc"}""")]
    [InlineData("""{"data":[1,"x"]}""")]
    [InlineData("""{"ax":[1,2],"ay":[1],"az":[1,2]}""")]
    [InlineData("""{"data":[1e300]}""")]
    [InlineData("[1,2,3]")]
    public void 壊れたメッセージは受け付けない(string json) => Assert.Null(Parse(json));

    [Fact]
    public void 多すぎる点を含むメッセージは受け付けない()
    {
        string big = "{\"data\":[" + string.Join(',', Enumerable.Repeat("0", SensorMessageParser.MaxSamplesPerMessage + 1)) + "]}";
        Assert.Null(Parse(big));
    }

    private static SensorPacket Packet(long seq, int count = 25) =>
        new([Enumerable.Repeat(0.01, count).ToArray(), new double[count], Enumerable.Repeat(1.0, count).ToArray()], "dev", seq, 50);

    [Fact]
    public void 最初の数秒を捨てて決まった長さだけ記録する()
    {
        var buffer = new RecordingBuffer(Condition.Rest, 50, durationSeconds: 10, settleSeconds: 2);
        long seq = 0;
        while (!buffer.IsComplete)
        {
            buffer.Add(Packet(seq));
            seq += 25;
        }
        var rec = buffer.ToRecording();
        Assert.Equal(500, rec.Length);
        Assert.Equal(0, rec.MissingSamples);
        Assert.Equal(600, seq); // 2 秒（100点）を捨て、10 秒（500点）を記録
    }

    [Fact]
    public void 通し番号の飛びを届かなかった点として数え_重複は捨てる()
    {
        var buffer = new RecordingBuffer(Condition.Rest, 50, durationSeconds: 10, settleSeconds: 0);
        buffer.Add(Packet(0));
        buffer.Add(Packet(25));
        buffer.Add(Packet(25));   // 重複
        buffer.Add(Packet(100));  // 50〜99 の 50 点が届かなかった
        Assert.Equal(75, buffer.Collected);
        Assert.Equal(50, buffer.MissingSamples);
    }

    [Fact]
    public void 終わり間際の途切れは_記録の残りの分だけを欠けとして数える()
    {
        // 10 秒（500 点）のうち 475 点を集めたところで 2 秒（100 点）途切れた → 欠けは残りの 25 点だけ
        var buffer = new RecordingBuffer(Condition.Rest, 50, durationSeconds: 10, settleSeconds: 0);
        for (long seq = 0; seq < 475; seq += 25) buffer.Add(Packet(seq));
        buffer.Add(Packet(575));
        Assert.True(buffer.IsComplete);
        Assert.Equal(475, buffer.Collected);
        Assert.Equal(25, buffer.MissingSamples);
    }

    [Fact]
    public void センサーが再起動して番号が大きく戻ったら_数え直して記録を続ける()
    {
        var buffer = new RecordingBuffer(Condition.Rest, 50, durationSeconds: 10, settleSeconds: 0);
        buffer.Add(Packet(100_000));
        buffer.Add(Packet(100_025));
        buffer.Add(Packet(0));   // 再起動
        buffer.Add(Packet(25));
        Assert.Equal(100, buffer.Collected);
        Assert.Equal(0, buffer.MissingSamples);
    }
}
