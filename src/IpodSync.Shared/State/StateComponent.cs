using Microsoft.AspNetCore.Components;

namespace IpodSync.Shared.State;

/// <summary>Re-renders whenever <see cref="AppState"/> changes (from any thread).</summary>
public abstract class StateComponent : ComponentBase, IDisposable
{
    [Inject] protected AppState State { get; set; } = default!;

    protected override void OnInitialized() => State.Changed += OnStateChanged;

    private void OnStateChanged() => _ = InvokeAsync(StateHasChanged);

    public virtual void Dispose()
    {
        State.Changed -= OnStateChanged;
        GC.SuppressFinalize(this);
    }

    protected static string Size(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):F2} GB" : $"{bytes / (double)(1L << 20):F0} MB";
    protected static string Time(int ms) { var t = TimeSpan.FromMilliseconds(ms); return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss"); }
}
