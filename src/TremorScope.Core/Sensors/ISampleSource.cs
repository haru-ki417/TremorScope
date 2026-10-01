namespace TremorScope.Core.Sensors;

/// <summary>センサーの値を受け取る口（IoT Hub、見本の信号など）</summary>
public interface ISampleSource : IAsyncDisposable
{
    /// <summary>画面に出す名前</summary>
    string Name { get; }

    /// <summary>届いた値を順に返す。キャンセルされるまで続く</summary>
    IAsyncEnumerable<SensorPacket> ReadAsync(CancellationToken cancellationToken);
}
