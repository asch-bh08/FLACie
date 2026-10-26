using FLACie.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace FLACie.Server.Components.Shared;

/// <summary>What every listening scene (car stereo, hi-fi, boombox, MiniDisc system) shows and does: the song playing, its time, what comes next, your
/// playlists, the player's controls, and its EQ. The scenes only draw it.</summary>
public abstract class RetroBase : UserComponent
{
    [Inject] protected IJSRuntime Js { get; set; } = default!;

    // ---------------------------------------------------------------- the EQ: the player's own five bands (flacie.js eq(): 100 Hz, 400 Hz, 1 kHz, 3 kHz and
    // 8 kHz, -12 to +12 dB, kept for next time), the same the desk's preamp sets: whatever a scene's sliders, knobs or preset key show is what the music goes
    // through. Read once the scene is on show (and MEGA BASS, the Discman's, with it); set at once here, then as the player took it.
    /// <summary>The bands as set, in dB (flat until the browser has answered).</summary>
    protected int[] Eq { get; private set; } = [0, 0, 0, 0, 0];
    /// <summary>MEGA BASS (flacie.js bass(): about +9 dB more on the bass band, not kept): whether it is on now.</summary>
    protected bool Mega { get; set; }
    protected static readonly string[] EqHz = ["100", "400", "1k", "3k", "8k"];
    protected static string Db(int v) => v == 0 ? "flat" : $"{(v > 0 ? "+" : "")}{v} dB";

    protected override async Task OnAfterRenderAsync(bool first)
    {
        if (!first) return;
        try
        {
            var st = await Js.InvokeAsync<EqState>("retro.eqState");
            if (st.Bands is { Length: 5 } b) Eq = b;
            Mega = st.Mega;
            StateHasChanged();
        }
        catch (Exception e) when (Gone(e)) { }
    }
    sealed record EqState(int[] Bands, bool Mega);

    /// <summary>Sets all five bands (a preset).</summary>
    protected async Task SetEq(int[] bands)
    {
        var next = bands.Select(v => Math.Clamp(v, -12, 12)).ToArray(); Eq = next;   // (shown at once: two quick moves add up before the player answers)
        try { var got = await Js.InvokeAsync<int[]>("flacie.eq", next); if (got is { Length: 5 } && ReferenceEquals(Eq, next)) Eq = got; } catch (Exception e) when (Gone(e)) { }
    }
    /// <summary>Sets one band.</summary>
    protected Task SetBand(int i, int v) { var next = (int[])Eq.Clone(); next[i] = v; return SetEq(next); }
    // the EQ key: the next sound of SceneEq's round (FLAT, ROCK, POP...) on the player's own EQ; a setting made by hand elsewhere (the boombox's sliders...) is kept to come back to
    int[]? eqCustom;
    protected string EqName => SceneEq.NameOf(Eq);
    protected Task EqKey()
    {
        if (EqName == SceneEq.Custom) eqCustom = (int[])Eq.Clone();
        return SetEq(SceneEq.Next(Eq, eqCustom));
    }
    /// <summary>A band's slider moved (a range input's value).</summary>
    protected Task BandInput(ChangeEventArgs e, int i) => int.TryParse(e.Value?.ToString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var v) ? SetBand(i, v) : Task.CompletedTask;
    /// <summary>A band turned or slid by the wheel: a decibel a notch.</summary>
    protected Task BandWheel(WheelEventArgs e, int i) => SetBand(i, Eq[i] + (e.DeltaY < 0 ? 1 : -1));
    /// <summary>A band's knob, by the keys (or a drag: retro.js turns it into arrow keys).</summary>
    protected Task BandKey(KeyboardEventArgs e, int i) => e.Key switch
    {
        "ArrowUp" or "ArrowRight" => SetBand(i, Eq[i] + 1), "ArrowDown" or "ArrowLeft" => SetBand(i, Eq[i] - 1),
        "Home" or "0" => SetBand(i, 0), _ => Task.CompletedTask,
    };

    // ---------------------------------------------------------------- the volume: the player's own (as the iPod's, the desk's preamp's)
    // (a knob or slider keeps where it was put: two quick turns add up even before the player has taken the first)
    double? volWant;
    protected double VolNow => volWant ?? Player.Volume;
    protected int VolPct => (int)Math.Round(VolNow * 100);
    protected async Task Vol(double v)
    {
        v = Math.Clamp(Math.Round(v * 20) / 20, 0, 1); volWant = v;
        await Player.SetVolumeAsync(v);
        if (volWant == v) volWant = null;
    }
    protected Task VolInput(ChangeEventArgs e) => double.TryParse(e.Value?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? Vol(v / 100) : Task.CompletedTask;
    protected Task VolWheel(WheelEventArgs e) => Vol(VolNow + (e.DeltaY < 0 ? .05 : -.05));
    protected Task VolKey(KeyboardEventArgs e) => e.Key switch
    {
        "ArrowUp" or "ArrowRight" => Vol(VolNow + .05), "ArrowDown" or "ArrowLeft" => Vol(VolNow - .05),
        "Home" => Vol(0), "End" => Vol(1), _ => Task.CompletedTask,
    };

    protected static bool Gone(Exception e) => e is JSDisconnectedException or JSException or InvalidOperationException or OperationCanceledException;
    protected static string Inv(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The playing song's lyrics and what the file is (from the page).</summary>
    [CascadingParameter] protected RetroSong? Song { get; set; }
    /// <summary>What the file is, short enough for a small display ("FLAC 16/44.1 1000K"); empty with nothing playing.</summary>
    protected string Format => Now is { } t ? DeskFormat.Short(Song?.Info, t) : "";
    /// <summary>The playing song's lyrics, for <see cref="RetroLyrics"/>.</summary>
    protected RenderFragment LyricsBox(string? fallback, string? cls = null) => b =>
    {
        b.OpenComponent<RetroLyrics>(0);
        b.AddComponentParameter(1, nameof(RetroLyrics.Lyrics), Song?.Lyrics);
        b.AddComponentParameter(2, nameof(RetroLyrics.SongKey), Song?.SongKey);
        b.AddComponentParameter(3, nameof(RetroLyrics.Fallback), fallback);
        b.AddComponentParameter(4, nameof(RetroLyrics.Class), cls);
        b.CloseComponent();
    };

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
    protected IReadOnlyList<PlaylistTape> Playlists
    {
        get
        {
            if (Session is null) return [];   // (the first render comes before the session is known)
            var key = string.Join(",", Session.Playlists.Select(p => $"{p.Id}:{p.Modified}:{p.Entries.Count}"));
            if (key != tapesKey) { tapesKey = key; tapes = Session.Playlists.OrderByDescending(p => p.Modified).Select(p => new PlaylistTape(p.Id, p.Name, Session.Resolve(p.Entries).Count)).Where(t => t.Count > 0).ToList(); }
            return tapes;
        }
    }
    List<PlaylistTape> tapes = []; string tapesKey = "";

    protected Task Toggle() => Player.Toggle();
    protected Task Next() => Player.Next();
    protected Task Prev() => Player.Prev();
    protected async Task Pause() { if (Player.Playing) await Player.Toggle(); }
    protected async Task Resume() { if (!Player.Playing && Player.Current is not null) await Player.Toggle(); }
    protected Task Seek(double fraction) => Player.Duration > 0 ? Player.Seek(Math.Clamp(fraction, 0, 1) * Player.Duration) : Task.CompletedTask;
    protected Task SetVolume(double v) => Vol(v);

    /// <summary>Plays one of your playlists from its first song.</summary>
    protected async Task PlayPlaylist(PlaylistTape tape)
    {
        if (Session.Playlists.FirstOrDefault(p => p.Id == tape.Id) is { } p && Session.Resolve(p.Entries) is { Count: > 0 } tracks) await Player.Play(tracks, 0, p.Name, DjSwitches.KeyOf(p));
    }
    /// <summary>Plays an album coming up: the whole album when shuffling, otherwise the queue goes on from it.</summary>
    protected async Task PlayAlbum(DeskAlbum a) { if (Player.Shuffle) await Player.PlayRun(a.Tracks); else await Player.SkipTo(a.QueueIndex); }

    protected static string Clock(double seconds) { if (!(seconds >= 0)) seconds = 0; var t = TimeSpan.FromSeconds(seconds); return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss"); }
    /// <summary>A stable number from a text (the car's "station", a tape's counter start): the same song always tunes to the same frequency.</summary>
    protected static int Hash(string? s) { unchecked { var h = 17; foreach (var c in s ?? "") h = h * 31 + c; return h & 0x7fffffff; } }
}
