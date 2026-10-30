using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace FLACie.Core;

/// <summary>
/// What the DJ knows about one person right now, shared by "talk to the DJ" (the ask prompt) and the line between songs (<see cref="DjWriter"/>), so the two
/// sound like one presenter: the session (the last songs and whether they were skipped, which way the energy is going, how long they have been listening, where,
/// what time it is), the conversation (the last few asks and answers, and the DJ's own last lines so it doesn't repeat itself), the person (top artists, what
/// they play most, what they asked for last) and the engine's reasons for its picks. Only in memory: the words asked are forgotten after <see cref="TalkLife"/>.
/// Thread-safe (one lock per person).
/// </summary>
public sealed class DjContext
{
    public const int SongsKept = 10, TalkKept = 4, LinesKept = 4;
    /// <summary>How long an ask is remembered (so "play more from them" works), and how long a request still steers the lines.</summary>
    public static readonly TimeSpan TalkLife = TimeSpan.FromMinutes(10);
    /// <summary>A gap this long starts a new listening session.</summary>
    public static readonly TimeSpan SessionGap = TimeSpan.FromMinutes(30);

    public sealed record Song(string Path, string Title, string Artist, bool Skipped, double? Energy, DateTime At);
    public sealed record Talk(string Asked, string Answered, DateTime At);

    readonly object gate = new();
    readonly List<Song> songs = [];
    readonly List<Talk> talk = [];
    readonly List<string> lines = [];
    readonly Dictionary<string, string> whys = new(StringComparer.Ordinal);
    DateTime? started, lastSeen;
    (string Words, DateTime At)? request;
    (string Artist, DateTime At)? subject;

    /// <summary>Where they are listening (the last surface seen).</summary>
    public DjSurface? Surface { get; private set; }
    /// <summary>The person's top artists and most played songs, filled by the server (null: not known).</summary>
    public Func<(IReadOnlyList<string> Artists, IReadOnlyList<string> Songs)>? Taste { get; set; }

    void Touch(DateTime now)
    {
        if (lastSeen is { } l && now - l > SessionGap) { songs.Clear(); started = null; }
        started ??= now; lastSeen = now;
    }

    /// <summary>A song finished or was skipped.</summary>
    public void Played(string path, string title, string artist, bool skipped, double? energy, DateTime now, DjSurface? surface = null)
    {
        lock (gate)
        {
            Touch(now);
            if (surface is { } s) Surface = s;
            if (songs.Count > 0 && songs[^1].Path == path && path.Length > 0) songs.RemoveAt(songs.Count - 1);
            songs.Add(new(path, title, artist, skipped, energy, now));
            if (songs.Count > SongsKept) songs.RemoveAt(0);
        }
    }

    public void Seen(DjSurface surface, DateTime now) { lock (gate) { Touch(now); Surface = surface; } }

    /// <summary>The engine's reason for picking [path] (said back for "why did you pick this?").</summary>
    public void Picked(string path, string why) { if (path.Length == 0 || why.Length == 0) return; lock (gate) { whys[path] = why; if (whys.Count > 50) whys.Remove(whys.Keys.First()); } }
    public string? WhyOf(string? path) { if (path is null) return null; lock (gate) return whys.GetValueOrDefault(path); }

    public void Asked(string asked, string answered, DateTime now)
    {
        lock (gate)
        {
            Touch(now);
            talk.Add(new(Clip(asked, 200), Clip(answered, 300), now));
            if (talk.Count > TalkKept) talk.RemoveAt(0);
        }
    }

    /// <summary>What they asked to hear ("car songs", "something chill"): later lines may keep it going.</summary>
    public void Requested(string words, DateTime now) { lock (gate) request = (Clip(words, 80), now); }
    /// <summary>The artist the conversation is about (for "them", "him", "her").</summary>
    public void About(string artist, DateTime now) { if (artist.Length > 0) lock (gate) subject = (artist, now); }

    public void Said(string line) { if (line.Length == 0) return; lock (gate) { lines.Add(Clip(line, 300)); if (lines.Count > LinesKept) lines.RemoveAt(0); } }

    public string? Request(DateTime now) { lock (gate) return request is { } r && now - r.At < TalkLife ? r.Words : null; }
    public string? Subject(DateTime now) { lock (gate) return subject is { } s && now - s.At < TalkLife ? s.Artist : null; }
    public IReadOnlyList<Talk> Conversation(DateTime now) { lock (gate) return talk.Where(t => now - t.At < TalkLife).ToList(); }
    public IReadOnlyList<string> Lines { get { lock (gate) return lines.ToList(); } }
    public IReadOnlyList<Song> Songs { get { lock (gate) return songs.ToList(); } }

    /// <summary>Forgets everything said (asks, answers, the request and the subject).</summary>
    public void Forget() { lock (gate) { talk.Clear(); request = null; subject = null; } }

    static string Clip(string s, int n) { s = (s ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim(); return s.Length <= n ? s : s[..n]; }

    /// <summary>Rising, falling or steady energy over the last songs that have a known energy (null when fewer than three).</summary>
    public static string? Trend(IReadOnlyList<Song> s)
    {
        var e = s.Where(x => x.Energy is not null).Select(x => x.Energy!.Value).TakeLast(5).ToList();
        if (e.Count < 3) return null;
        var d = e[^1] - e[0];
        return d > 0.15 ? "building up (more energetic)" : d < -0.15 ? "winding down (calmer)" : "steady";
    }

    /// <summary>
    /// The compact, grounded summary that goes into both prompts. [localHour] is the listener's hour (null: not known). Only what is held here; nothing is
    /// guessed, and empty parts are left out. [engineChose]: the engine picked the next song; when it is the listener's own (a playlist, an album, their queue) the
    /// "(skipped)" tags and the energy trend are left out, so the writer cannot remark on skips or on a direction nobody chose (owner, 30 Oct: "because you skipped
    /// this song, playing this" in a playlist).
    /// </summary>
    public string Summary(DateTime now, int? localHour = null, bool withTalk = true, bool engineChose = true)
    {
        var sb = new StringBuilder();
        void Put(string k, string? v) { if (!string.IsNullOrWhiteSpace(v)) sb.Append("- ").Append(k).Append(": ").Append(v).Append('\n'); }
        List<Song> ss; List<Talk> ts; List<string> ls; string? req, subj; DateTime? from; DjSurface? surf;
        lock (gate)
        {
            if (lastSeen is { } l && now - l > SessionGap) { ss = []; from = null; } else { ss = songs.ToList(); from = started; }
            ts = talk.Where(t => now - t.At < TalkLife).ToList(); ls = lines.ToList(); surf = Surface;
            req = request is { } r && now - r.At < TalkLife ? r.Words : null;
            subj = subject is { } s && now - s.At < TalkLife ? s.Artist : null;
        }
        Put("Listening on", surf switch { DjSurface.Auto => "the car", DjSurface.Phone => "their phone", DjSurface.Web => "the web player", DjSurface.Windows => "their computer", _ => null });
        Put("Time of day", localHour switch { >= 5 and <= 11 => "morning", >= 12 and <= 17 => "afternoon", >= 18 and <= 22 => "evening", null => null, _ => "late at night" });
        if (from is { } f && now - f >= TimeSpan.FromMinutes(5)) Put("Listening for", Math.Round((now - f).TotalMinutes).ToString(CultureInfo.InvariantCulture) + " minutes");
        if (ss.Count > 0)
            Put("Last songs (oldest first)", string.Join("; ", ss.Select(x => (x.Title.Length > 0 ? x.Title : "?") + (x.Artist.Length > 0 ? " by " + x.Artist : "") + (x.Skipped && engineChose ? " (skipped)" : ""))));
        if (engineChose) Put("Energy", Trend(ss));
        Put("They asked for", req);
        Put("Talking about", subj);
        if (Taste?.Invoke() is { } taste)
        {
            Put("Their top artists", string.Join(", ", taste.Artists.Take(5)));
            Put("What they play most", string.Join("; ", taste.Songs.Take(3)));
        }
        if (withTalk && ts.Count > 0)
        {
            sb.Append("- Recent conversation:\n");
            foreach (var t in ts) sb.Append("  - They said: ").Append(t.Asked).Append(" / You answered: ").Append(t.Answered).Append('\n');
        }
        if (ls.Count > 0) Put("Your last lines (never repeat them)", string.Join(" | ", ls));
        return sb.ToString();
    }
}

/// <summary>One <see cref="DjContext"/> per account, in memory only.</summary>
public sealed class DjContexts
{
    readonly ConcurrentDictionary<string, DjContext> all = new(StringComparer.Ordinal);
    public Action<string, DjContext>? Made { get; set; }
    public DjContext For(string account) => all.GetOrAdd(account, k => { var c = new DjContext(); Made?.Invoke(k, c); return c; });
}
