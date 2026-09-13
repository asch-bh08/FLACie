using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;

/// <summary>
/// Gathers candidate signing inputs. Candidates are never trusted as-is:
/// DeviceSigning only accepts a FirewireGuid that reproduces an existing hash58
/// and a hash72 key that reproduces an existing signature.
/// </summary>
static class SigningInputs
{
    /// <summary>16-hex-digit serials of Apple USB devices Windows has seen
    /// (USB\VID_05AC&amp;PID_xxxx\&lt;serial&gt;). For iPods this serial is the FirewireGuid.</summary>
    public static List<string> FirewireCandidates(IEnumerable<string> explicitIds)
    {
        var list = explicitIds.ToList();
        if (OperatingSystem.IsWindows()) list.AddRange(FromRegistry());
        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> FromRegistry()
    {
        var found = new List<string>();
        try
        {
            using var usb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
            if (usb is null) return found;
            foreach (var dev in usb.GetSubKeyNames().Where(n => n.StartsWith("VID_05AC", StringComparison.OrdinalIgnoreCase)))
            {
                using var k = usb.OpenSubKey(dev);
                if (k is null) continue;
                found.AddRange(k.GetSubKeyNames().Where(s => Regex.IsMatch(s, "^[0-9A-Fa-f]{16}$")));
            }
        }
        catch { /* registry unavailable: explicit ids only */ }
        return found;
    }

    /// <summary>iTunes-written databases kept in local backups, usable as proof of a
    /// device's signing material (matched by library id inside DeviceSigning).</summary>
    public static List<string> References(string backupRoot, IEnumerable<string> explicitRefs)
    {
        var list = explicitRefs.ToList();
        try
        {
            if (Directory.Exists(backupRoot))
                list.AddRange(Directory.EnumerateFiles(backupRoot, "iTunes*DB", new EnumerationOptions
                {
                    RecurseSubdirectories = true, MaxRecursionDepth = 4, IgnoreInaccessible = true,
                }).Where(f => Path.GetFileName(f) is "iTunesCDB" or "iTunesDB"));
        }
        catch { /* best effort */ }
        return list;
    }
}
