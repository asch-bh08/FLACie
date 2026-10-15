using FLACie.Core;

namespace FLACie.Server.Components.Shared;

/// <summary>What every listening scene (car stereo, hi-fi, boombox, MiniDisc system) shows and does: the song playing, its time, what comes next, your
/// playlists, and the player's controls. The scenes only draw it.</summary>
public abstract class RetroBase : UserComponent
{
    protected Track? Now => Player.Current;
    protected bool Playing => Player.Playing;
    protected double Position => Player.Position;
    protected double Length => Player.Duration;
    /// <summary>How far into the song, 0 to 1.</summary>
    protected double Progress => Player.Duration > 0 ? Math.Clamp(Player.Position / Player.Duration, 0, 1) : 0;
    /// <summary>Which song of the queue, counted from 1 in play order (0 when nothing plays).</summary>
    protected int TrackNo { get { var o = Player.PlayOrder; for (var i = 0; i < o.Count; i++) if (o[i] == Player.Index) return i + 1; return Player.Current is null ? 0 : 1; } }
    protected int TrackCount => Player.Queue.Count;
    /// <summary>The albums coming up (in play order, each once, the playing one left out; round again with repeat all).</summary>
    protected IReadOnlyList<DeskAlbum> NextAlbums => DeskSources.Next(Player.Queue, Player.PlayOrder, Player.Index, 4, Player.Repeat == 1);
    protected DeskAlbum? NowAlbum => DeskSources.Current(Player.Queue, Player.Index);

    /// <summary>Your playlists that have songs in this library, newest first.</summary>
    protected IReadOnlyList<DeskTapes.Tape> Playlists
    {
        get
        {
            if (Session is null) return [];   // (the first render comes before the session is known)
            var key = string.Join(",", Session.Playlists.Select(p => $"{p.Id}:{p.Modified}:{p.Entries.Count}"));
            if (key != tapesKey) { tapesKey = key; tapes = Session.Playlists.OrderByDescending(p => p.Modified).Select(p => new DeskTapes.Tape(p.Id, p.Name, Session.Resolve(p.Entries).Count)).Where(t => t.Count > 0).ToList(); }
            return tapes;
        }
    }
    List<DeskTapes.Tape> tapes = []; string tapesKey = "";

    protected Task Toggle() => Player.Toggle();
    protected Task Next() => Player.Next();
    protected Task Prev() => Player.Prev();
    protected async Task Pause() { if (Player.Playing) await Player.Toggle(); }
    protected async Task Resume() { if (!Player.Playing && Player.Current is not null) await Player.Toggle(); }
    protected Task Seek(double fraction) => Player.Duration > 0 ? Player.Seek(Math.Clamp(fraction, 0, 1) * Player.Duration) : Task.CompletedTask;
    protected Task SetVolume(double v) => Player.SetVolumeAsync(Math.Clamp(v, 0, 1));

    /// <summary>Plays one of your playlists from its first song.</summary>
    protected async Task PlayPlaylist(DeskTapes.Tape tape)
    {
        if (Session.Playlists.FirstOrDefault(p => p.Id == tape.Id) is { } p && Session.Resolve(p.Entries) is { Count: > 0 } tracks) await Player.Play(tracks, 0, p.Name);
    }
    /// <summary>Plays an album coming up: the whole album when shuffling, otherwise the queue goes on from it.</summary>
    protected async Task PlayAlbum(DeskAlbum a) { if (Player.Shuffle) await Player.PlayRun(a.Tracks); else await Player.SkipTo(a.QueueIndex); }

    protected static string Clock(double seconds) { if (!(seconds >= 0)) seconds = 0; var t = TimeSpan.FromSeconds(seconds); return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss"); }
    /// <summary>A stable number from a text (the car's "station", a tape's counter start): the same song always tunes to the same frequency.</summary>
    protected static int Hash(string? s) { unchecked { var h = 17; foreach (var c in s ?? "") h = h * 31 + c; return h & 0x7fffffff; } }
}
