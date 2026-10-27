namespace FLACie.Core;

/// <summary>What the player does about something said to the DJ (docs/dj-ask-report.md). The server works it out; the players only carry it out.</summary>
public enum DjAskDo { None, Play, UpNext, Next, Pause, Resume, VolumeUp, VolumeDown, DjOn, DjOff }

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
}

public static class DjAskRunner
{
    /// <summary>How much "turn it up" and "turn it down" move the volume.</summary>
    public const double VolumeStep = 0.15;

    /// <summary>Carries the answer out on [p]. True when it changed anything.</summary>
    public static async Task<bool> RunAsync(IDjAskPlayer p, DjAskAnswer a)
    {
        switch (a.Do)
        {
            case DjAskDo.Play when a.Paths is { Count: > 0 } paths: await p.PlayAsync(paths, a.Title ?? "", a.Shuffle); return true;
            case DjAskDo.UpNext when a.Paths is { Count: > 0 } paths: await p.UpNextAsync(paths, a.Replace); return true;
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
