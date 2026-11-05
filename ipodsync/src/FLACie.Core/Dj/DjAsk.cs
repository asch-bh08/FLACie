namespace FLACie.Core;

/// <summary>What the player does about something said to the DJ (docs/dj-ask-report.md). The server works it out; the players only carry it out.</summary>
public enum DjAskDo { None, Play, UpNext, Next, Pause, Resume, VolumeUp, VolumeDown, DjOn, DjOff, AddToPlaylist, RemoveFromPlaylist, CreatePlaylist, Ask, Shuffle }

/// <summary>
/// One step of an answer (F9). [Paths] are the songs it is about; [Playlist] the playlist's name for the playlist steps; [Say] the question for
/// <see cref="DjAskDo.Ask"/> (the player says it and opens the mic again); [Shuffle] turns shuffle on or off for <see cref="DjAskDo.Shuffle"/>.
/// </summary>
public sealed record DjAskStep(DjAskDo Do, IReadOnlyList<string>? Paths = null, string? Title = null, bool Shuffle = false, bool Replace = false,
    string? Playlist = null, string? Say = null)
{
    public static DjAskStep AddTo(string playlist, IReadOnlyList<string> paths) => new(DjAskDo.AddToPlaylist, paths, Playlist: playlist);
    public static DjAskStep RemoveFrom(string playlist, IReadOnlyList<string> paths) => new(DjAskDo.RemoveFromPlaylist, paths, Playlist: playlist);
    public static DjAskStep Create(string name, IReadOnlyList<string> paths) => new(DjAskDo.CreatePlaylist, paths, Playlist: name);
    public static DjAskStep Question(string say) => new(DjAskDo.Ask, Say: say);
}

/// <summary>A song the library lacked and that is being downloaded: the player plays it when it lands.</summary>
public sealed record DjAskWait(string Artist, string Title);

/// <summary>
/// The DJ's answer to one request. [Say] is the short line in the DJ's voice ("" for plain controls like Next); [Clip] the address of that line said by
/// the DJ's voice, or null (then the player shows [Say] as a toast, or says it with the device's voice when [DeviceVoice]). [Paths] are library paths:
/// a new queue for <see cref="DjAskDo.Play"/>, the songs to put after the current one for <see cref="DjAskDo.UpNext"/> (replacing Up next when [Replace]).
/// </summary>
public sealed record DjAskAnswer(DjAskDo Do, string Say, IReadOnlyList<string>? Paths = null, string? Title = null, bool Shuffle = false, bool Replace = false,
    DjAskWait? Wait = null, string? Intent = null)
{
    public string? Clip { get; init; }
    public bool DeviceVoice { get; init; }
    /// <summary>The steps to carry out in order (F9); null for an old single-action answer, which is then its one step.</summary>
    public IReadOnlyList<DjAskStep>? Steps { get; init; }
    public IReadOnlyList<DjAskStep> AllSteps => Steps ?? (Do == DjAskDo.None ? [] : [new(Do, Paths, Title, Shuffle, Replace)]);
    public static DjAskAnswer Said(string say, string? intent = null) => new(DjAskDo.None, say, Intent: intent);
}

/// <summary>The player an answer is carried out on (the web player; a fake in the tests). The phone has its own twin in Kotlin.</summary>
public interface IDjAskPlayer
{
    Task PlayAsync(IReadOnlyList<string> paths, string title, bool shuffle);
    Task UpNextAsync(IReadOnlyList<string> paths, bool replace);
    Task NextAsync();
    Task PauseAsync();
    Task ResumeAsync();
    /// <summary>Changes the volume by [delta] (-1 to 1 of the whole range).</summary>
    Task NudgeVolumeAsync(double delta);
    Task SetDjAsync(bool on);
    // The F9 steps; a player that can't do one leaves it alone.
    Task AddToPlaylistAsync(string playlist, IReadOnlyList<string> paths) => Task.CompletedTask;
    Task RemoveFromPlaylistAsync(string playlist, IReadOnlyList<string> paths) => Task.CompletedTask;
    Task CreatePlaylistAsync(string name, IReadOnlyList<string> paths) => Task.CompletedTask;
    /// <summary>Says [question] and opens the mic again for the answer.</summary>
    Task AskAsync(string question) => Task.CompletedTask;
    Task ShuffleAsync(bool on) => Task.CompletedTask;
}

public static class DjAskRunner
{
    /// <summary>How much "turn it up" and "turn it down" move the volume.</summary>
    public const double VolumeStep = 0.15;

    /// <summary>Carries the answer's steps out on [p], in order. True when it changed anything.</summary>
    public static async Task<bool> RunAsync(IDjAskPlayer p, DjAskAnswer a)
    {
        var any = false;
        foreach (var step in a.AllSteps) any |= await RunAsync(p, step);
        return any;
    }

    public static async Task<bool> RunAsync(IDjAskPlayer p, DjAskStep a)
    {
        switch (a.Do)
        {
            case DjAskDo.Play when a.Paths is { Count: > 0 } paths: await p.PlayAsync(paths, a.Title ?? "", a.Shuffle); return true;
            case DjAskDo.UpNext when a.Paths is { Count: > 0 } paths: await p.UpNextAsync(paths, a.Replace); return true;
            case DjAskDo.AddToPlaylist when a.Paths is { Count: > 0 } paths && a.Playlist is { Length: > 0 } pl: await p.AddToPlaylistAsync(pl, paths); return true;
            case DjAskDo.RemoveFromPlaylist when a.Paths is { Count: > 0 } paths && a.Playlist is { Length: > 0 } pl: await p.RemoveFromPlaylistAsync(pl, paths); return true;
            case DjAskDo.CreatePlaylist when a.Playlist is { Length: > 0 } pl: await p.CreatePlaylistAsync(pl, a.Paths ?? []); return true;
            case DjAskDo.Ask when a.Say is { Length: > 0 } q: await p.AskAsync(q); return true;
            case DjAskDo.Shuffle: await p.ShuffleAsync(a.Shuffle); return true;
            case DjAskDo.Next: await p.NextAsync(); return true;
            case DjAskDo.Pause: await p.PauseAsync(); return true;
            case DjAskDo.Resume: await p.ResumeAsync(); return true;
            case DjAskDo.VolumeUp: await p.NudgeVolumeAsync(VolumeStep); return true;
            case DjAskDo.VolumeDown: await p.NudgeVolumeAsync(-VolumeStep); return true;
            case DjAskDo.DjOn: await p.SetDjAsync(true); return true;
            case DjAskDo.DjOff: await p.SetDjAsync(false); return true;
            default: return false;
        }
    }
}
