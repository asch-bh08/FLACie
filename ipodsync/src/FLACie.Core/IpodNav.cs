namespace FLACie.Core;

/// <summary>One line of an iPod menu. <see cref="Id"/> tells the page what it opens or plays; <see cref="Opens"/> draws the right-hand arrow; <see cref="Value"/> is the setting shown on the right; <see cref="Checked"/> draws a tick (or room for one).</summary>
/// <summary>What a list holds: the screen picks its look from it (an artwork pane for songs, albums and artists; Cover Flow; plain settings rows).</summary>
public enum IpodKind { Menu, Songs, Albums, Artists, Settings, Memos, CoverFlow, Generic }

public sealed record IpodRow(string Label, string Id, bool Opens = true, string? Value = null, bool? Checked = null, string? ArtKey = null, string? Sub = null, string? Detail = null);

/// <summary>
/// The menu stack of the iPod mode (web and Windows): titles, rows, the highlighted row and the scroll position per level, driven by wheel turns (whole detents),
/// the centre button and MENU. Pure, so it is unit-tested; the page decides what a pressed row does (open a list or play).
/// </summary>
public sealed class IpodNav(int visibleRows = 6)
{
    sealed class Frame(string title, IReadOnlyList<IpodRow> rows, IpodKind kind) { public string Title = title; public IReadOnlyList<IpodRow> Rows = rows; public IpodKind Kind = kind; public int Selected; public int Top; }

    readonly List<Frame> stack = [];
    int visible = Math.Max(2, visibleRows);
    /// <summary>Rows that fit the screen (the family and size decide); changing it keeps every level's highlight on screen.</summary>
    public int VisibleRows { get => visible; set { visible = Math.Max(2, value); foreach (var f in stack) Scroll(f); } }
    public IpodKind Kind => stack.Count == 0 ? IpodKind.Menu : stack[^1].Kind;
    /// <summary>+1 after going deeper, -1 after going back, 0 otherwise: the page uses it for the slide direction.</summary>
    public int LastMove { get; private set; }

    public int Depth => stack.Count;
    public string Title => stack.Count == 0 ? "" : stack[^1].Title;
    public IReadOnlyList<IpodRow> Rows => stack.Count == 0 ? [] : stack[^1].Rows;
    public int Selected => stack.Count == 0 ? -1 : stack[^1].Selected;
    public int Top => stack.Count == 0 ? 0 : stack[^1].Top;
    public IpodRow? Current => stack.Count == 0 || Rows.Count == 0 ? null : Rows[Selected];
    public IEnumerable<IpodRow> Visible => Rows.Skip(Top).Take(VisibleRows);
    /// <summary>The scrollbar thumb as fractions of the track (start, length), or null when everything fits.</summary>
    public (double Start, double Length)? Thumb => Rows.Count <= VisibleRows ? null : ((double)Top / Rows.Count, (double)VisibleRows / Rows.Count);

    public void Push(string title, IReadOnlyList<IpodRow> rows, IpodKind kind = IpodKind.Menu) { stack.Add(new Frame(title, rows, kind)); LastMove = 1; }
    /// <summary>The highlighted row of a level by depth (0 = the main menu), or -1.</summary>
    public int SelectedAt(int depth) => depth >= 0 && depth < stack.Count ? stack[depth].Selected : -1;
    /// <summary>Move the highlight of the current level straight to a row (a tap on it).</summary>
    public bool Select(int index) { if (stack.Count == 0 || index < 0 || index >= Rows.Count) return false; var f = stack[^1]; f.Selected = index; Scroll(f); LastMove = 0; return true; }
    /// <summary>Drop every level above the first (MENU held: back to the main menu).</summary>
    public bool ToRoot() { if (stack.Count <= 1) return false; stack.RemoveRange(1, stack.Count - 1); LastMove = -1; return true; }
    /// <summary>Replace the rows of the current level (a list that finished loading) keeping the highlight where it was.</summary>
    public void Replace(IReadOnlyList<IpodRow> rows)
    {
        if (stack.Count == 0) return;
        var f = stack[^1]; f.Rows = rows; f.Selected = Math.Clamp(f.Selected, 0, Math.Max(0, rows.Count - 1)); Scroll(f);
    }

    /// <summary>Replace the rows of a lower level (the main menu, after a setting or a song started changed what it lists).</summary>
    public void ReplaceAt(int depth, IReadOnlyList<IpodRow> rows)
    {
        if (depth < 0 || depth >= stack.Count) return;
        var f = stack[depth]; f.Rows = rows; f.Selected = Math.Clamp(f.Selected, 0, Math.Max(0, rows.Count - 1)); Scroll(f);
    }

    /// <summary>MENU: one level up; false at the top (nothing to go back to).</summary>
    public bool Back() { if (stack.Count <= 1) return false; stack.RemoveAt(stack.Count - 1); LastMove = -1; return true; }
    public void Reset() { stack.Clear(); LastMove = 0; }

    /// <summary>Wheel turn by whole detents (positive = clockwise = down). Stops at the ends like the hardware.</summary>
    public bool Turn(int detents)
    {
        if (stack.Count == 0 || Rows.Count == 0 || detents == 0) return false;
        var f = stack[^1]; var was = f.Selected;
        f.Selected = Math.Clamp(f.Selected + detents, 0, f.Rows.Count - 1); Scroll(f); LastMove = 0;
        return f.Selected != was;
    }

    void Scroll(Frame f)
    {
        if (f.Selected < f.Top) f.Top = f.Selected;
        else if (f.Selected >= f.Top + VisibleRows) f.Top = f.Selected - VisibleRows + 1;
        f.Top = Math.Clamp(f.Top, 0, Math.Max(0, f.Rows.Count - VisibleRows));
    }
}

/// <summary>Turns finger movement on the wheel into whole detents: 15 degrees each, the remainder carried so slow turns still click.</summary>
public sealed class WheelDetents(double degreesPerDetent = 15)
{
    double carry;
    public double DegreesPerDetent { get; set; } = degreesPerDetent > 0 ? degreesPerDetent : 15;
    /// <summary>Angle change since the last event, in degrees (wraps at +/-180 are folded in).</summary>
    public int Feed(double deltaDegrees)
    {
        while (deltaDegrees > 180) deltaDegrees -= 360;
        while (deltaDegrees < -180) deltaDegrees += 360;
        carry += deltaDegrees;
        var n = (int)Math.Truncate((carry + Math.Sign(carry) * 0.05) / DegreesPerDetent);   // a hair of tolerance: 30 degrees measured as 29.99999 is still two clicks
        carry -= n * DegreesPerDetent;
        return n;
    }
    /// <summary>Same, with the speed-up of a fast spin: <paramref name="gain"/> multiplies the movement (see <see cref="WheelFeel.Gain"/>).</summary>
    public int Feed(double deltaDegrees, double gain) => Feed(deltaDegrees * Math.Max(1, gain));
    public void Release() => carry = 0;
}
