namespace FLACie.Core;

public enum JackEdge { Top, Bottom }

/// <summary>Where an iPod's headphone jack is: the edge, and how far across the body (0 left, 1 right, looking at the face).</summary>
public sealed record DeskJack(JackEdge Edge, double X);

/// <summary>Where the earbuds plug in on each iPod of the emulator, so the iPod desk's cable leaves from the right place. Pure and unit-tested.</summary>
public static class DeskJacks
{
    static readonly DeskJack TopLeft = new(JackEdge.Top, 0.2), MiniTop = new(JackEdge.Top, 0.16), BottomLeft = new(JackEdge.Bottom, 0.2), BottomMiddle = new(JackEdge.Bottom, 0.5);

    /// <summary>The 1st to 5th generations, the classics and the minis have the jack on the top; the nanos and the touches on the bottom (the 6th-gen nano in the middle).</summary>
    public static DeskJack For(string modelId) => modelId switch
    {
        "mini" or "mini2" => MiniTop,
        "nano6" => BottomMiddle,
        _ when modelId.StartsWith("nano", StringComparison.Ordinal) || modelId.StartsWith("touch", StringComparison.Ordinal) => BottomLeft,
        _ => TopLeft,
    };

    /// <summary>The iPods with the 30-pin dock connector: the 3rd generation on, the minis, nanos 1 to 6 and touches 1 to 4 (the 1st and 2nd generations had FireWire,
    /// the 7th nano and the 5th touch on Lightning).</summary>
    public static bool HasDock(string modelId) => modelId is not ("ipod1" or "ipod2" or "nano7" or "touch5" or "touch6" or "touch7");
}
