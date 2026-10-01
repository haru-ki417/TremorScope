using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;

namespace TremorScope.Infrastructure.Pacs;

/// <summary>PACS の接続先</summary>
public sealed record PacsSettings(string Host, int Port, string CallingAeTitle, string CalledAeTitle);

/// <summary>PACS（Orthanc など）へ DICOM を送る</summary>
public sealed class PacsClient(PacsSettings settings)
{
    /// <summary>接続の確認（C-ECHO）</summary>
    public async Task<bool> EchoAsync(CancellationToken cancellationToken = default)
    {
        bool ok = false;
        var client = Create();
        var request = new DicomCEchoRequest { OnResponseReceived = (_, response) => ok = response.Status == DicomStatus.Success };
        await client.AddRequestAsync(request);
        await client.SendAsync(cancellationToken);
        return ok;
    }

    /// <summary>画像を送る（C-STORE）。失敗したら理由を含む例外を投げる</summary>
    public async Task StoreAsync(DicomFile file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        DicomStatus? status = null;
        var client = Create();
        var request = new DicomCStoreRequest(file) { OnResponseReceived = (_, response) => status = response.Status };
        await client.AddRequestAsync(request);
        await client.SendAsync(cancellationToken);
        if (status != DicomStatus.Success)
            throw new InvalidOperationException($"PACS が受け取りませんでした（{status?.Description ?? "応答なし"}）。");
    }

    private IDicomClient Create()
    {
        if (string.IsNullOrWhiteSpace(settings.Host) || settings.Port is <= 0 or > 65535)
            throw new InvalidOperationException("PACS のホスト名とポート番号を設定してください。");
        var client = DicomClientFactory.Create(settings.Host, settings.Port, false, settings.CallingAeTitle, settings.CalledAeTitle);
        client.ClientOptions.AssociationRequestTimeoutInMs = 5000;
        return client;
    }
}
