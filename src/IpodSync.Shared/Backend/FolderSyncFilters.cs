namespace IpodSync.Shared.Backend;

public static class FolderSyncFilters
{
    private static readonly HashSet<string> Native = new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".m4a", ".m4b", ".aac", ".wav", ".aif", ".aiff" };

    /// <summary>Formats the iPod plays as-is (by container; an M4A holding something other
    /// than AAC/ALAC is caught later by the write pipeline's dry run).</summary>
    public static bool IsNative(string path) => Native.Contains(Path.GetExtension(path));
}
