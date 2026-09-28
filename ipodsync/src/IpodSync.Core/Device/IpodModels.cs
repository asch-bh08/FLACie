namespace IpodSync.Core.Device;

/// <summary>
/// Model numbers seen in <c>SysInfo</c>/<c>SysInfoExtended</c> (<c>ModelNumStr</c>), for naming a
/// device in the UI. This is a hint only: what the app will actually do with an iPod is decided
/// from the device's own database header (see <see cref="IpodProfiler"/>), never from this table,
/// so an unlisted model is named "iPod" and still works if its database says it can.
///
/// Sources: pypodlib's device matrix and libgpod's device table — see COMPATIBILITY.md.
/// </summary>
public static class IpodModels
{
    private static readonly Dictionary<string, string> ByModelNumber = new(StringComparer.OrdinalIgnoreCase)
    {
        // --- no database signature (pre-2007) ---
        ["MA146"] = "iPod mini (1st gen)", ["MA188"] = "iPod mini (1st gen)",
        ["MA205"] = "iPod mini (1st gen)", ["MA206"] = "iPod mini (1st gen)",
        ["MA214"] = "iPod mini (1st gen)", ["MA215"] = "iPod mini (1st gen)",
        ["MA216"] = "iPod mini (1st gen)", ["MA217"] = "iPod mini (1st gen)",
        ["MA233"] = "iPod mini (2nd gen)", ["MA234"] = "iPod mini (2nd gen)",
        ["MA235"] = "iPod mini (2nd gen)", ["MA236"] = "iPod mini (2nd gen)",
        ["MA350"] = "iPod (5th gen, video)", ["MA444"] = "iPod (5th gen, video)",
        ["MA445"] = "iPod (5th gen, video)", ["MA446"] = "iPod (5th gen, video)",

        // --- hash58 (FirewireGuid) ---
        ["MB029"] = "iPod (5.5 gen)", ["MB147"] = "iPod (5.5 gen)", ["MB148"] = "iPod (5.5 gen)",
        ["MB149"] = "iPod (5.5 gen)", ["MB150"] = "iPod (5.5 gen)", ["MB562"] = "iPod (5.5 gen)",
        ["MA632"] = "iPod classic (6th gen)", ["MA633"] = "iPod classic (6th gen)",
        ["MC293"] = "iPod classic 120GB (6th gen)", ["MC297"] = "iPod classic (7th gen)",
        ["MA978"] = "iPod nano (3rd gen)", ["MA979"] = "iPod nano (3rd gen)",
        ["MB754"] = "iPod nano (4th gen)", ["MB755"] = "iPod nano (4th gen)",

        // --- hash58 + hash72, and the first with the SQLite bundle ---
        ["MC027"] = "iPod nano (5th gen)", ["MC028"] = "iPod nano (5th gen)",
        ["MC059"] = "iPod nano (5th gen)", ["MC060"] = "iPod nano (5th gen)",
        ["MC112"] = "iPod nano (5th gen)", ["MC275"] = "iPod nano (5th gen)",

        // --- hashAB: readable, not writable (see COMPATIBILITY.md) ---
        ["MC525"] = "iPod nano (6th gen)", ["MC526"] = "iPod nano (6th gen)",
        ["MKMX2"] = "iPod nano (7th gen)",
    };

    /// <summary>A model name for a <c>ModelNumStr</c> like "xA978" or "MB754", or null if unlisted.</summary>
    public static string? Name(string? modelNumber)
    {
        if (string.IsNullOrWhiteSpace(modelNumber)) return null;
        string code = new([.. modelNumber.Trim().Where(char.IsLetterOrDigit)]);
        if (code.Length > 1 && (code[0] == 'x' || code[0] == 'X')) code = code[1..];
        if (ByModelNumber.TryGetValue(code, out var exact)) return exact;
        // SysInfo sometimes carries a suffix (MB754LL): match the leading code.
        foreach (var (key, name) in ByModelNumber)
            if (code.StartsWith(key, StringComparison.OrdinalIgnoreCase)) return name;
        return null;
    }
}
