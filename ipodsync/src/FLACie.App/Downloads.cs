using FLACie.Core;

namespace FLACie.Server;

public sealed class DownloadJob(string id, string label, string? artKey, string kind = "Search", string artist = "", string title = "", string album = "", long durationMs = 0, string? artUrl = null, bool isAlbum = false)
{
    public string Album { get; } = album;
    public long DurationMs { get; } = durationMs;
    public string? ArtUrl { get; } = artUrl;
    public bool IsAlbum { get; } = isAlbum;
    public string Id { get; } = id;
    public string Label { get; } = label;
    public string Artist { get; } = artist;
    public string Title { get; } = title;
    public string? ArtKey { get; } = artKey;
    /// <summary>Where it came from: Search (the user asked), Autoplay (fetched ahead for the queue), Import (a playlist file) or Chart.</summary>
    public string Kind { get; } = kind;
    public DateTime At { get; } = DateTime.UtcNow;
    string? key;
    /// <summary>What the song is matched by (title and lead artist, like the rest of the app); empty for a job with no song name.</summary>
    public string Key => key ??= Title.Length > 0 ? Matching.MatchKey(Title, Artist) : "";
    public DateTime? FinishedAt { get; internal set; }
    public DownloadStage Stage { get; internal set; } = DownloadStage.Requested;
    public string Message { get; internal set; } = "Requested";
    /// <summary>The source being worked on right now (soulseek, lidarr, ytdl, archive, audius, jamendo), null before the first one starts.</summary>
    public string? Current { get; internal set; }
    /// <summary>The source that delivered it, once it is done.</summary>
    public string? Source { get; internal set; }
    public string? File { get; internal set; }
    public List<TrailStep> Trail { get; } = [];
    public bool Finished => Stage is DownloadStage.Done or DownloadStage.Failed;

    /// <summary>Takes one status from the download code: moves the job along and keeps the step for the story shown later.</summary>
    internal void Apply(DownloadStatus st)
    {
        lock (Trail)
        {
            if (!st.Miss) { Stage = st.Stage; Message = st.Message; if (st.Source is not null) Current = st.Source; }
            if (st.Stage == DownloadStage.Done) { Source = st.Source; if (st.NewTracks is { Count: > 0 } t) File = FileOf(t[0].Path); }
            if (Trail.Count == 0 || Trail[^1].Text != st.Message || Trail[^1].Source != st.Source) { Trail.Add(new(DateTime.UtcNow, st.Source, st.Message, st.Miss)); if (Trail.Count > 40) Trail.RemoveAt(1); }
        }
    }

    static string? FileOf(string path) { var i = path.IndexOf("path=", StringComparison.Ordinal); return i < 0 ? null : Uri.UnescapeDataString(path[(i + 5)..]); }

    internal DownloadRecord ToRecord(UserSession s) => new()
    {
        Id = Id, UserId = s.Id, UserName = s.DisplayName, Label = Label, Title = Title, Artist = Artist, Kind = Kind, StartedAt = At, FinishedAt = FinishedAt ?? DateTime.UtcNow,
        Outcome = Stage == DownloadStage.Done ? "done" : "failed", Source = Stage == DownloadStage.Done ? Source ?? Current : null, Message = Message, File = File, ArtKey = ArtKey,
        Album = Album, DurationMs = DurationMs, ArtUrl = ArtUrl, IsAlbum = IsAlbum, Trail = [.. Trail],
    };
}
