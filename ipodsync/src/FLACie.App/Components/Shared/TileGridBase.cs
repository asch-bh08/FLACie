using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace FLACie.Server.Components.Shared;

/// <summary>A grid of tiles (Albums, Artists) that draws only the rows on or near the screen. With 20,000 songs the Albums page drew all 1,200 tiles: 2.6 MB
/// sent and 12,000 elements built on every visit. The browser (flacie.js tileGrid) tells the server the grid's columns, the distance from one row to the next
/// and the rows to draw; the rows above and below are the grid's own padding, exactly as tall as they would be, so the scroll bar, the columns at every width
/// and Back's place in the page are as before. Tiles are keyed, so one stays the same element (its focus, its loaded cover) while it is on screen.</summary>
public abstract class TileGridBase : UserComponent
{
    [Inject] IJSRuntime Js { get; set; } = default!;
    protected ElementReference GridEl;
    DotNetObjectReference<TileGridBase>? self;

    /// <summary>Tiles drawn before the browser has measured the grid (the server-drawn page, the first draw): enough to fill a big screen.</summary>
    internal const int FirstTiles = 60;
    int cols, firstRow, endRow; double pitch;
    bool all;
    /// <summary>Changes whenever the rows to draw change (so a grid that skips redraws for the player still draws them).</summary>
    protected int WindowVersion { get; private set; }

    // the last measure in this tab, per kind of grid: the next grid of that kind starts with the right height and rows (Back to Albums)
    sealed record Geo(int Cols, double Pitch, int Rows);
    static readonly ConditionalWeakTable<object, Dictionary<Type, Geo>> lastGeo = new();

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        lock (lastGeo)
            if (lastGeo.TryGetValue(Player, out var m) && m.TryGetValue(GetType(), out var g)) { cols = g.Cols; pitch = g.Pitch; firstRow = 0; endRow = g.Rows; }
    }

    /// <summary>The tiles to draw (Start..End) of <paramref name="count"/>, and the grid's padding that stands in for the rows above and below.</summary>
    protected (int Start, int End, string? Style) Window(int count)
    {
        if (all || count == 0) return (0, count, null);
        if (cols <= 0 || pitch <= 0)
        {
            // not measured yet: the first tiles, from the top, and a guess at the rest's height (6 columns of 260 px) until the browser says
            var end = Math.Min(count, FirstTiles);
            var rest = (int)Math.Ceiling((count - end) / 6.0) * 260;
            return (0, end, rest > 0 ? $"padding-bottom:{rest}px" : null);
        }
        var rows = (count + cols - 1) / cols;
        var r0 = Math.Clamp(firstRow, 0, rows);
        var r1 = Math.Clamp(endRow, r0, rows);
        if (r1 == r0) r1 = Math.Min(rows, r0 + 1);
        var top = r0 * pitch; var bottom = (rows - r1) * pitch;
        var style = (top > 0 ? $"padding-top:{Px(top)}px;" : "") + (bottom > 0 ? $"padding-bottom:{Px(bottom)}px" : "");
        return (r0 * cols, Math.Min(count, r1 * cols), style.Length > 0 ? style : null);
    }

    static string Px(double v) => Math.Round(v, 2).ToString(System.Globalization.CultureInfo.InvariantCulture);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);
        if (!firstRender) return;
        self = DotNetObjectReference.Create(this);
        try { await Js.InvokeVoidAsync("flacie.tileGrid", GridEl, self); }
        catch (JSDisconnectedException) { }
        catch (Exception) { all = true; WindowVersion++; StateHasChanged(); }   // no measuring in this browser: every tile, as before
    }

    /// <summary>From the browser: the grid's columns, the distance from one row's top to the next, and the rows to draw (from, up to).</summary>
    [JSInvokable] public Task Seen(int c, double p, int from, int to)
    {
        if (c <= 0 || p <= 0 || to <= from) return Task.CompletedTask;
        lock (lastGeo) lastGeo.GetOrCreateValue(Player)[GetType()] = new Geo(c, p, to - from);
        if (c == cols && p == pitch && from == firstRow && to == endRow) return Task.CompletedTask;
        cols = c; pitch = p; firstRow = from; endRow = to;
        WindowVersion++;
        return InvokeAsync(StateHasChanged);
    }

    public override void Dispose()
    {
        base.Dispose();
        self?.Dispose();
    }
}
