using CommunityToolkit.Mvvm.ComponentModel;
using TremorScope.Infrastructure.Cloud;

namespace TremorScope.App.Services;

/// <summary>
/// まだクラウドに送っていない測定を Cosmos DB に送る。
/// 送れなかったものは残しておき、次の機会（測定の保存後・起動時・手動）に送り直す。
/// </summary>
public sealed partial class CloudSyncService(AppServices services) : ObservableObject
{
    private readonly SemaphoreSlim gate = new(1, 1);

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    public partial bool HasProblem { get; set; }

    public async Task SyncPendingAsync(CancellationToken cancellationToken = default)
    {
        if (!await gate.WaitAsync(0, cancellationToken)) return;
        try
        {
            using var cloud = services.CreateCloud();
            if (cloud is null)
            {
                Status = services.Settings.CloudEnabled ? "クラウド: 設定が不足しています" : "クラウド保存: オフ";
                HasProblem = services.Settings.CloudEnabled;
                return;
            }
            var pending = await services.Store.PendingCloudSyncAsync(cancellationToken);
            int sent = 0;
            foreach (var id in pending)
            {
                var session = await services.Store.GetSessionAsync(id, cancellationToken);
                if (session is null) continue;
                var doc = CloudSync.ToDocument(session.Value.Summary, session.Value.Recordings, AppServices.AppVersion);
                await cloud.UpsertAsync(doc, cancellationToken);
                await services.Store.MarkCloudSyncedAsync(id, cancellationToken);
                sent++;
            }
            Status = sent > 0 ? $"クラウド: {sent} 件を保存しました" : "クラウド: すべて保存済み";
            HasProblem = false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Status = "クラウド: 送れませんでした（あとで送り直します）";
            HasProblem = true;
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>患者を削除したときに、クラウド側の記録も消す</summary>
    public async Task<string?> DeletePatientAsync(string pseudonymId)
    {
        using var cloud = services.CreateCloud();
        if (cloud is null) return null;
        try
        {
            int n = await cloud.DeletePatientAsync(pseudonymId);
            return $"クラウドからも {n} 件を削除しました。";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            return $"クラウドから削除できませんでした。Azure Portal で仮名 ID「{pseudonymId}」の記録を削除してください。";
        }
    }
}
