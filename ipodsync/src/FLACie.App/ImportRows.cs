using FLACie.Core;

namespace FLACie.Server;

/// <summary>A song of an import the library does not have yet: where it goes in the list (before the playable song at [Before]) and its live state.</summary>
public sealed record WaitingSong(int Before, string Artist, string Title, string Status, bool Failed, int? Percent);

/// <summary>
/// F26.6 (owner, 5 Nov: the 99-song import showed only the 24 downloaded): an imported playlist's songs in the import's order. Songs in the library
/// are normal rows; the others are greyed in their place with their state: Waiting, Searching, Downloading 40 %, Couldn't find it (with Retry).
/// As a song lands it becomes a normal row where it was.
/// </summary>
public static class ImportRows
{
    public static (List<Track> Playable, List<WaitingSong> Waiting) Build(IReadOnlyList<ImportSong> songs, Func<string, string, Track?> find, Func<string, string, DownloadJob?> job)
    {
        var playable = new List<Track>(); var waiting = new List<WaitingSong>(); var seen = new HashSet<string>();
        foreach (var s in songs)
        {
            if (find(s.Artist, s.Title) is { } t)
            {
                if (seen.Add(t.Path)) playable.Add(t);
                continue;
            }
            var j = job(s.Artist, s.Title);
            waiting.Add(StatusOf(playable.Count, s, j));
        }
        return (playable, waiting);
    }

    static WaitingSong StatusOf(int before, ImportSong s, DownloadJob? j)
    {
        if (j is { Stage: DownloadStage.Failed } || (j is null or { Finished: true } && s.State == "failed"))
        {
            var why = s.Message ?? j?.Message;
            return new(before, s.Artist, s.Title, why is { Length: > 0 } && !why.StartsWith("Couldn't") ? $"Couldn't find it ({why})" : "Couldn't find it", true, null);
        }
        if (j is { Stage: DownloadStage.Downloading or DownloadStage.Importing or DownloadStage.Scanning })
        {
            var pc = System.Text.RegularExpressions.Regex.Match(j.Message, @"(\d{1,3})\s?%") is { Success: true } p ? int.Parse(p.Groups[1].Value) : (int?)null;
            return new(before, s.Artist, s.Title, j.Stage == DownloadStage.Downloading ? "Downloading" + (pc is { } x ? $" {x} %" : "") : "Filing into the library", false, pc);
        }
        if (j is { Stage: DownloadStage.Searching }) return new(before, s.Artist, s.Title, "Searching", false, null);
        if (s.State == "requested") return new(before, s.Artist, s.Title, "Requested from Lidarr", false, null);
        return new(before, s.Artist, s.Title, "Waiting", false, null);
    }
}
