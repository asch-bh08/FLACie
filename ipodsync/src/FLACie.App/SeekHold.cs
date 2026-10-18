using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace FLACie.Server;

/// <summary>
/// What a seek slider shows. The server re-renders the slider with the playback position every half second, which used to drag the thumb
/// out of the listener's hand mid-drag and snap it back to the old spot just after a seek (until playback reported the new time).
/// While the thumb is held, and for a moment after a seek, the slider keeps the listener's value instead.
/// </summary>
public sealed class SeekHold
{
    double held;
    DateTime downAt = DateTime.MinValue, until = DateTime.MinValue;

    bool Down => DateTime.UtcNow - downAt < TimeSpan.FromSeconds(30);   // never stuck on if a pointer-up goes missing

    public void Press() => downAt = DateTime.UtcNow;
    public void Release() => downAt = DateTime.MinValue;
    public void Input(ChangeEventArgs e) { if (Parse(e.Value, out var v)) { held = v; downAt = DateTime.UtcNow; } }
    public void Changed(double s) { held = s; downAt = DateTime.MinValue; until = DateTime.UtcNow.AddSeconds(2); }

    /// <summary>The value to draw: the held one while dragging or until playback reaches the new spot, else the real position.</summary>
    public double Value(double position)
    {
        if (Down) return held;
        if (DateTime.UtcNow < until) { if (Math.Abs(position - held) > 1.5) return held; until = DateTime.MinValue; }
        return position;
    }

    public static bool Parse(object? v, out double d) => double.TryParse(v?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out d);
}
