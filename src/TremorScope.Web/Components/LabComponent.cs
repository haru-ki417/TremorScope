using Microsoft.AspNetCore.Components;
using TremorScope.Web.State;

namespace TremorScope.Web.Components;

/// <summary>状態が変わったら描き直す部品の元</summary>
public abstract class LabComponent : ComponentBase, IDisposable
{
    [Inject] protected Lab L { get; set; } = default!;

    protected override void OnInitialized() => L.Changed += OnLabChanged;

    protected virtual void OnLabChanged() => InvokeAsync(StateHasChanged);

    protected static string F(double v, string format = "0.#") => Labels.F(v, format);

    public virtual void Dispose()
    {
        L.Changed -= OnLabChanged;
        GC.SuppressFinalize(this);
    }
}
