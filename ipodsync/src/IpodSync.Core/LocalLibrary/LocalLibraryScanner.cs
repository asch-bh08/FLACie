namespace IpodSync.Core.LocalLibrary;

/// <summary>Scans a folder tree for audio files. Read-only, and has nothing to do
/// with any iPod -- this is "what music does the user have," full stop.</summary>
public static class LocalLibraryScanner
{
    private static readonly string[] SupportedExtensions = [".mp3", ".m4a", ".flac", ".aac", ".wav", ".alac", ".aiff"];

    public static List<LocalTrack> Scan(string folder)
    {
        var results = new List<LocalTrack>();
        if (!Directory.Exists(folder)) return results;

        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            string ext = Path.GetExtension(file);
            if (!SupportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) continue;
            results.Add(ReadOne(file, ext));
        }
        return results;
    }

    private static LocalTrack ReadOne(string file, string ext)
    {
        long size = 0;
        try { size = new FileInfo(file).Length; } catch (IOException) { }

        try
        {
            using var tf = TagLib.File.Create(file);
            return new LocalTrack
            {
                Path = file,
                Title = tf.Tag.Title,
                Artist = tf.Tag.FirstPerformer,
                Album = tf.Tag.Album,
                Duration = tf.Properties.Duration,
                SizeBytes = size,
                Extension = ext.TrimStart('.').ToUpperInvariant(),
            };
        }
        catch (Exception)
        {
            // Unreadable or corrupt tag data -- still list the file by name
            // rather than dropping it silently from the library view.
            return new LocalTrack { Path = file, SizeBytes = size, Extension = ext.TrimStart('.').ToUpperInvariant() };
        }
    }
}
