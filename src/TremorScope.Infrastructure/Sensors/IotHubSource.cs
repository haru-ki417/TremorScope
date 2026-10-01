using System.Runtime.CompilerServices;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using TremorScope.Core.Sensors;

namespace TremorScope.Infrastructure.Sensors;

/// <summary>
/// Azure IoT Hub に届いたセンサーの値を受け取る（IoT Hub の「イベント ハブ互換エンドポイント」を使う）。
///
/// ・接続文字列は、読み取りだけができる共有アクセス ポリシー（service）のものを使う。
///   iothubowner（すべての権限を持つ）は、漏れたときの被害が大きいので使わない。
/// ・受信は「今」から始める。過去にたまっていたメッセージは読まない。
/// ・デバイス ID を指定すると、そのセンサーの値だけを使う（ほかのセンサーの値が混ざらないように）。
/// </summary>
public sealed class IotHubSource : ISampleSource
{
    private readonly EventHubConsumerClient client;
    private readonly string? deviceId;

    public IotHubSource(string eventHubCompatibleConnectionString, string consumerGroup, string? deviceId)
    {
        var check = ConnectionStringCheck.Inspect(eventHubCompatibleConnectionString);
        if (!check.IsValid) throw new ArgumentException(check.Problem, nameof(eventHubCompatibleConnectionString));
        client = new EventHubConsumerClient(string.IsNullOrWhiteSpace(consumerGroup) ? EventHubConsumerClient.DefaultConsumerGroupName : consumerGroup,
            eventHubCompatibleConnectionString);
        this.deviceId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId.Trim();
    }

    public string Name => deviceId is null ? "IoT Hub" : $"IoT Hub（{deviceId}）";

    public async IAsyncEnumerable<SensorPacket> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var options = new ReadEventOptions { MaximumWaitTime = TimeSpan.FromSeconds(2) };
        await foreach (var e in client.ReadEventsAsync(startReadingAtEarliestEvent: false, options, cancellationToken))
        {
            if (e.Data is null) continue; // 待ち時間内に何も届かなかった
            if (deviceId is not null && SenderOf(e.Data) is string sender && !string.Equals(sender, deviceId, StringComparison.Ordinal)) continue;
            if (SensorMessageParser.TryParse(e.Data.EventBody.ToMemory().Span, out var packet) && packet is not null)
                yield return packet with { DeviceId = packet.DeviceId ?? SenderOf(e.Data), ReceivedAt = e.Data.EnqueuedTime };
        }
    }

    /// <summary>IoT Hub が付ける送信元デバイスの ID</summary>
    private static string? SenderOf(EventData data) =>
        data.SystemProperties.TryGetValue("iothub-connection-device-id", out var v) ? v as string : null;

    public ValueTask DisposeAsync() => client.DisposeAsync();
}

/// <summary>接続文字列の確認結果</summary>
public sealed record ConnectionStringCheck(bool IsValid, string? Problem, string? Warning, string? PolicyName)
{
    /// <summary>形と権限を確かめる（鍵の値そのものは見ない・記録しない）</summary>
    public static ConnectionStringCheck Inspect(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return new(false, "接続文字列が入力されていません。", null, null);
        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.OrdinalIgnoreCase);

        if (!parts.TryGetValue("Endpoint", out var endpoint) || !endpoint.StartsWith("sb://", StringComparison.OrdinalIgnoreCase))
            return new(false, "「Endpoint=sb://…」で始まる、イベント ハブ互換の接続文字列を入力してください（IoT Hub の「組み込みのエンドポイント」にあります）。", null, null);
        if (!parts.TryGetValue("SharedAccessKeyName", out var policy) || !parts.ContainsKey("SharedAccessKey"))
            return new(false, "接続文字列に SharedAccessKeyName と SharedAccessKey が含まれていません。", null, null);
        if (!parts.ContainsKey("EntityPath"))
            return new(false, "接続文字列に EntityPath が含まれていません。「組み込みのエンドポイント」の接続文字列をそのまま貼り付けてください。", null, null);

        string? warning = policy.Equals("iothubowner", StringComparison.OrdinalIgnoreCase)
            ? "すべての権限を持つ「iothubowner」の鍵です。読み取りだけができる「service」ポリシーの接続文字列に替えることをおすすめします。"
            : null;
        return new(true, null, warning, policy);
    }
}
