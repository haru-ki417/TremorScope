using System.Net;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using TremorScope.Core.Analysis;
using TremorScope.Core.Domain;

namespace TremorScope.Infrastructure.Cloud;

/// <summary>Azure Cosmos DB の接続先</summary>
public sealed record CloudSettings(string Endpoint, string Key, string Database, string Container);

/// <summary>
/// クラウドに保存する内容。カルテ番号・メモなど、個人を特定しうる情報は含めない。
/// </summary>
public sealed class CloudMeasurementDocument
{
    [JsonProperty("id")] public required string Id { get; init; }
    [JsonProperty("pseudonymId")] public required string PseudonymId { get; init; }
    [JsonProperty("measuredAtUtc")] public required DateTime MeasuredAtUtc { get; init; }
    [JsonProperty("hand")] public required string Hand { get; init; }
    [JsonProperty("pattern")] public required string Pattern { get; init; }
    [JsonProperty("algorithmVersion")] public required string AlgorithmVersion { get; init; }
    [JsonProperty("appVersion")] public required string AppVersion { get; init; }
    [JsonProperty("conditions")] public required List<CloudCondition> Conditions { get; init; }
}

public sealed class CloudCondition
{
    [JsonProperty("condition")] public required string Condition { get; init; }
    [JsonProperty("peakFrequencyHz")] public double PeakFrequencyHz { get; init; }
    [JsonProperty("rmsAccelerationMg")] public double RmsAccelerationMg { get; init; }
    [JsonProperty("estimatedDisplacementMm")] public double EstimatedDisplacementMm { get; init; }
    [JsonProperty("regularity")] public double Regularity { get; init; }
    [JsonProperty("bands")] public required Dictionary<string, double> Bands { get; init; }
    [JsonProperty("quality")] public required string Quality { get; init; }
    [JsonProperty("sampleRate")] public double SampleRate { get; init; }
    [JsonProperty("samples")] public required float[][] Samples { get; init; }
}

/// <summary>測定結果を Azure Cosmos DB に保存する（仮名 ID をパーティションキーにする）</summary>
public sealed class CloudSync(CloudSettings settings) : IDisposable
{
    private readonly CosmosClient client = new(settings.Endpoint, settings.Key, new CosmosClientOptions
    {
        ApplicationName = "TremorScope",
        SerializerOptions = new CosmosSerializationOptions { PropertyNamingPolicy = CosmosPropertyNamingPolicy.Default },
    });
    private Container? container;

    public static CloudMeasurementDocument ToDocument(SessionSummary session, IReadOnlyList<Recording> recordings, string appVersion)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(recordings);
        var conditions = new List<CloudCondition>();
        foreach (var (metrics, condition) in new[] { (session.Rest, Condition.Rest), (session.Postural, Condition.Postural) })
        {
            if (metrics is null) continue;
            var rec = recordings.FirstOrDefault(r => r.Condition == condition);
            conditions.Add(new CloudCondition
            {
                Condition = condition.ToString(),
                PeakFrequencyHz = Math.Round(metrics.PeakFrequencyHz, 3),
                RmsAccelerationMg = Math.Round(metrics.RmsAccelerationMg, 3),
                EstimatedDisplacementMm = Math.Round(metrics.EstimatedDisplacementMm, 4),
                Regularity = Math.Round(metrics.Regularity, 4),
                Bands = metrics.Bands.ToDictionary(b => $"{b.LowHz:0}-{b.HighHz:0}Hz", b => Math.Round(b.Share, 4)),
                Quality = metrics.Quality.Level.ToString(),
                SampleRate = rec?.SampleRate ?? 0,
                Samples = rec?.Axes.Select(a => a.Select(v => (float)Math.Round(v, 5)).ToArray()).ToArray() ?? [],
            });
        }
        return new CloudMeasurementDocument
        {
            Id = session.Id.ToString(),
            PseudonymId = session.PseudonymId,
            MeasuredAtUtc = session.MeasuredAtUtc,
            Hand = session.Hand.ToString(),
            Pattern = session.Comparison.Pattern.ToString(),
            AlgorithmVersion = TremorAnalyzer.AlgorithmVersion,
            AppVersion = appVersion,
            Conditions = conditions,
        };
    }

    /// <summary>保存する（同じ測定を何度送っても 1 件のまま）</summary>
    public async Task UpsertAsync(CloudMeasurementDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        var c = await GetContainerAsync(cancellationToken);
        await c.UpsertItemAsync(document, new PartitionKey(document.PseudonymId), cancellationToken: cancellationToken);
    }

    /// <summary>患者を削除したとき、クラウドからもその患者の測定をすべて消す</summary>
    public async Task<int> DeletePatientAsync(string pseudonymId, CancellationToken cancellationToken = default)
    {
        var c = await GetContainerAsync(cancellationToken);
        var query = new QueryDefinition("SELECT c.id FROM c WHERE c.pseudonymId = @p").WithParameter("@p", pseudonymId);
        int deleted = 0;
        using var iterator = c.GetItemQueryIterator<IdOnly>(query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(pseudonymId) });
        while (iterator.HasMoreResults)
        {
            foreach (var item in await iterator.ReadNextAsync(cancellationToken))
            {
                try
                {
                    await c.DeleteItemAsync<IdOnly>(item.Id, new PartitionKey(pseudonymId), cancellationToken: cancellationToken);
                    deleted++;
                }
                catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    // すでに消えている
                }
            }
        }
        return deleted;
    }

    /// <summary>接続の確認</summary>
    public async Task TestAsync(CancellationToken cancellationToken = default) => await GetContainerAsync(cancellationToken);

    private async Task<Container> GetContainerAsync(CancellationToken cancellationToken)
    {
        if (container is not null) return container;
        Database db = await client.CreateDatabaseIfNotExistsAsync(settings.Database, cancellationToken: cancellationToken);
        container = await db.CreateContainerIfNotExistsAsync(new ContainerProperties(settings.Container, "/pseudonymId"), cancellationToken: cancellationToken);
        return container;
    }

    private sealed class IdOnly
    {
        [JsonProperty("id")] public string Id { get; set; } = "";
    }

    public void Dispose() => client.Dispose();
}
