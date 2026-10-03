using Microsoft.JSInterop;

namespace TremorScope.Web.State;

/// <summary>ブラウザーとのやりとり（保存・印刷・この端末への記録・センサー・波形の表示）。どこにも送らない</summary>
public sealed class BrowserIo(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? io;
    private IJSObjectReference? motion;
    private IJSObjectReference? wave;

    private async ValueTask<IJSObjectReference> Io() => io ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/io.js");

    private async ValueTask<IJSObjectReference> Motion() => motion ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/motion.js");

    private async ValueTask<IJSObjectReference> Wave() => wave ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/wave.js");

    public async ValueTask DownloadAsync(string name, string mime, byte[] bytes) => await (await Io()).InvokeVoidAsync("download", name, mime, bytes);

    public async ValueTask PrintAsync() => await (await Io()).InvokeVoidAsync("print");

    public async ValueTask<string?> LoadAsync(string key) => await (await Io()).InvokeAsync<string?>("load", key);

    public async ValueTask<bool> SaveAsync(string key, string value) => await (await Io()).InvokeAsync<bool>("save", key, value);

    public async ValueTask ScrollTopAsync() => await (await Io()).InvokeVoidAsync("scrollTop");

    public async ValueTask<bool> MotionSupportedAsync() => await (await Motion()).InvokeAsync<bool>("supported");

    public async ValueTask<bool> MotionNeedsPermissionAsync() => await (await Motion()).InvokeAsync<bool>("needsPermission");

    public async ValueTask BindPermissionButtonAsync(string id, object dotnetRef) => await (await Motion()).InvokeVoidAsync("bindPermissionButton", id, dotnetRef);

    public async ValueTask StartMotionAsync(object dotnetRef) => await (await Motion()).InvokeVoidAsync("start", dotnetRef);

    public async ValueTask StopMotionAsync() => await (await Motion()).InvokeVoidAsync("stop");

    public async ValueTask KeepAwakeAsync(bool on) => await (await Motion()).InvokeVoidAsync("keepAwake", on);

    public async ValueTask BuzzAsync(int ms) => await (await Motion()).InvokeVoidAsync("buzz", ms);

    public async ValueTask WaveCreateAsync(string id) => await (await Wave()).InvokeVoidAsync("create", id);

    public async ValueTask WavePushAsync(string id, double[] values) => await (await Wave()).InvokeVoidAsync("push", id, values);

    public async ValueTask WaveResetAsync(string id) => await (await Wave()).InvokeVoidAsync("reset", id);

    public async ValueTask WaveColorAsync(string id, string color) => await (await Wave()).InvokeVoidAsync("color", id, color);

    public async ValueTask WaveDisposeAsync(string id) => await (await Wave()).InvokeVoidAsync("dispose", id);

    public async ValueTask DisposeAsync()
    {
        foreach (var m in new[] { io, motion, wave })
        {
            if (m is not null) await m.DisposeAsync();
        }
    }
}
