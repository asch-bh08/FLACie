namespace IpodSync.Core.LocalLibrary;

/// <summary>A track found on disk, independent of any iPod. This is the "iTunes
/// library" half of the app -- files the user owns, before anything about
/// syncing them to a device.</summary>
public sealed class LocalTrack
{
    public required string Path { get; init; }
    public string? Title { get; init; }
    public string? Artist { get; init; }
    public string? Album { get; init; }
    public TimeSpan Duration { get; init; }
    public long SizeBytes { get; init; }
    public string Extension { get; init; } = "";

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? System.IO.Path.GetFileNameWithoutExtension(Path) : Title;
}
