using System.Collections.Concurrent;
using System.Text.Json;
using FLACie.Core;

namespace FLACie.Server.Dj;

/// <summary>
/// The DJ for every player of this server: the web and Windows apps ask it in process, the phone through POST /api/dj/next. Each person's settings live in their
/// <see cref="UserState"/>; what the DJ remembers (the last lines, artists, skips) is kept in memory per account. A decision is kept for 30 minutes per
/// (account, previous song, next song), so asking again gives the same answer and moves the memory on only once.
/// </summary>
public sealed class DjService : IDjService
{
    public static readonly TimeSpan DecisionLife = TimeSpan.FromMinutes(30);
    public const string TestLine = "This is the FLACie DJ. Here's a test line.";

    readonly UserStateStore states; readonly CloudflareVoice voice; readonly DjClips clips; readonly ILogger<DjService> log; readonly Func<DateTime> clock;
    readonly string pronounceFile;
    readonly ConcurrentDictionary<string, DjMemory> memories = new();
    readonly ConcurrentDictionary<string, (DateTime At, DjLine? Line)> decisions = new();
    DjPronounce pronounce = DjPronounce.Default; DateTime pronounceStamp = DateTime.MinValue, pronounceChecked = DateTime.MinValue;
    int asks;

    public DjService(UserStateStore states, CloudflareVoice voice, DjClips clips, DataPaths paths, ILogger<DjService> log, Func<DateTime>? clock = null)
    {
        this.states = states; this.voice = voice; this.clips = clips; this.log = log; this.clock = clock ?? (() => DateTime.UtcNow);
        pronounceFile = Path.Combine(paths.Root, "dj-pronounce.tsv");
    }

    public bool VoiceReady => voice.Ready;

    static DjSettings Copy(DjSettings s)
    {
        var c = JsonSerializer.Deserialize<DjSettings>(JsonSerializer.Serialize(s)) ?? new();
        c.Playlists ??= new(); c.Songs ??= new();
        return c;
    }

    /// <summary>A copy of this person's settings (change it and hand it to <see cref="Save"/>).</summary>
    public DjSettings Settings(UserSession s) => Copy(states.For(s).Dj ?? new());

    public void Save(UserSession s, DjSettings settings)
    {
        var c = Copy(settings);
        // playlist keys are ids: no blanks, nothing huge, a sane number of them; two that are the same once trimmed are one playlist (the last one wins)
        var lists = new Dictionary<string, bool>();
        foreach (var (k, v) in c.Playlists)
        {
            var key = k.Trim();
            if (key.Length is > 0 and <= 200 && (lists.Count < 1000 || lists.ContainsKey(key))) lists[key] = v;
        }
        c.Playlists = lists;
        c.Songs = c.Songs.Where(x => x.Key.Trim().Length is > 0 and <= 700).Take(5000).ToDictionary(x => x.Key.Trim(), x => x.Value);
        c.Volume = DjRules.Clamp(c.Volume); c.Duck = DjRules.Clamp(c.Duck);
        var songsBefore = states.For(s).Dj?.Songs ?? new();
        states.For(s).Dj = c;
        states.Save(s);
        // a song's choice changed: decisions already made for this person are made again, so the next song follows it at once
        if (songsBefore.Count != c.Songs.Count || songsBefore.Any(x => !c.Songs.TryGetValue(x.Key, out var v) || v != x.Value))
            foreach (var k in decisions.Keys.Where(k => k.StartsWith(s.Id + "\n", StringComparison.Ordinal)).ToList()) { decisions.TryRemove(k, out _); written.TryRemove(k, out _); counted.TryRemove(k, out _); }
    }

    /// <summary>The pronunciation list, read again when <c>dj-pronounce.tsv</c> changes (looked at no more than once a minute).</summary>
    DjPronounce Pronounce()
    {
        var now = clock();
        if (now - pronounceChecked < TimeSpan.FromMinutes(1)) return pronounce;
        pronounceChecked = now;
        try
        {
            var stamp = File.Exists(pronounceFile) ? File.GetLastWriteTimeUtc(pronounceFile) : DateTime.MinValue;
            if (stamp != pronounceStamp) { pronounce = DjPronounce.Load(pronounceFile); pronounceStamp = stamp; }
        }
        catch (Exception) { }
        return pronounce;
    }

    static string SongKey(DjSong? t) => t is null ? "" : t.Path.Length > 0 ? t.Path : t.JellyfinId is { Length: > 0 } j ? "jf:" + j : "t:" + t.Artist + "|" + t.Title;

    /// <summary>Repeatable randomness per decision: the same account, songs and point in the memory give the same draw (string.GetHashCode changes per run, so FNV-1a).</summary>
    static int Seed(string key, int songs)
    {
        unchecked
        {
            var h = 2166136261u;
            foreach (var c in key) { h ^= c; h *= 16777619u; }
            h ^= (uint)songs; h *= 16777619u;
            return (int)(h & 0x7fffffff);
        }
    }

    /// <summary>What the server knows for certain about the next song, and the listener's bond with it (loved, new). Set where the DJ is registered (the vibe index,
    /// the MusicBrainz cache and the account's taste); null in tests that do not ask for it.</summary>
    public Func<UserSession, DjRequest, (DjExtra? Extra, DjBond Bond)>? Facts { get; set; }
    /// <summary>Make the pooled phrases of a voice and style ahead of time (on where the server registers the DJ; off in tests that count calls).</summary>
    public bool WarmPool { get; set; }
    readonly ConcurrentDictionary<string, bool> warmed = new();

    /// <summary>The first name for the hello, from the account's name when it looks like one.</summary>
    public static string? FirstNameOf(UserSession s) => DjNames.FirstName(s.Kind == "nas" ? s.Nas?.User : s.Jellyfin?.UserName);

    /// <summary>Decides once per (account, previous song, next song) within <see cref="DecisionLife"/>; the same line (or the same silence) after that.</summary>
    DjLine? Decide(UserSession s, DjSettings st, DjRequest req, DjExtra? extra, out string key, out DjMemory mem)
    {
        key = s.Id + "\n" + SongKey(req.Prev) + "\n" + SongKey(req.Next);
        var now = clock();
        mem = memories.GetOrAdd(s.Id, _ => new DjMemory());
        if (decisions.TryGetValue(key, out var d) && now - d.At < DecisionLife) return d.Line;
        lock (mem)
        {
            if (decisions.TryGetValue(key, out d) && now - d.At < DecisionLife) return d.Line;
            var line = DjPlanner.Plan(st, req, mem, now, new Random(Seed(key, mem.Songs)), Pronounce(), FirstNameOf(s), extra);
            decisions[key] = (now, line);
            written.TryRemove(key, out _); counted.TryRemove(key, out _);
            if (Interlocked.Increment(ref asks) % 200 == 0)
            {
                foreach (var old in decisions.Where(x => now - x.Value.At >= DecisionLife).Select(x => x.Key).ToList()) decisions.TryRemove(old, out _);
                foreach (var old in written.Where(x => now - x.Value.At >= DecisionLife).Select(x => x.Key).ToList()) { written.TryRemove(old, out _); counted.TryRemove(old, out _); }
            }
            return line;
        }
    }

    /// <summary>Lines are written by the text model when the dashboard allows it (turned on where the server registers the DJ; off in tests that do not ask for it).</summary>
    public bool AiLines { get; set; }
    /// <summary>How long the text model may take (<see cref="DjWriter.Timeout"/>; shorter in tests).</summary>
    public TimeSpan WriteTimeout { get; set; } = DjWriter.Timeout;
    /// <summary>Per decision: the line as it will be said, and whether it is pooled (kept phrases and a name clip) rather than one whole clip.</summary>
    readonly ConcurrentDictionary<string, (DateTime At, Lazy<Task<(DjLine Line, bool Pooled)>> Job)> written = new();
    /// <summary>Decisions already counted as a line said today.</summary>
    readonly ConcurrentDictionary<string, bool> counted = new();

    public async Task<DjCue?> PrepareAsync(UserSession s, DjRequest req, CancellationToken ct = default)
    {
        var st = Settings(s);
        // the switches are read fresh every time: turning the DJ off silences an answer given before
        if (!DjRules.EffectiveOn(st, req.Surface, req.PlaylistId, req.QueueDj)) return null;
        DjExtra? extra = null;
        try
        {
            if (Facts is { } facts)
            {
                var (x, bond) = facts(s, req);
                extra = x;
                if (req.Bond == DjBond.Unknown && bond != DjBond.Unknown) req = req with { Bond = bond };
            }
        }
        catch (Exception e) { log.LogDebug("DJ facts not read: {Kind}", e.GetType().Name); }
        DjLine? line; string key; DjMemory mem;
        try { line = Decide(s, st, req, extra, out key, out mem); }
        catch (Exception e) { log.LogWarning("DJ line not written: {Kind}", e.GetType().Name); return null; }
        if (line is null) return null;
        if (WarmPool && voice.Ready && voice.Current.Voice is var v && v != CloudflareVoice.MeloTts && warmed.TryAdd(v + "\n" + st.Style, true))
            _ = Task.Run(() => clips.WarmAsync(DjPool.All(st.Style)));
        // the planner said when; the budget says whole or pooled; the text model says how (once per decision; the template line on any failure)
        var planned = line; var name = FirstNameOf(s);
        var (said, pooled) = await written.GetOrAdd(key, _ => (clock(), new Lazy<Task<(DjLine, bool)>>(() => VoiceAsync(req, planned, mem, st.Style, extra, name)))).Job.Value;
        // the person chose the device's own voice: every line is said by it, and no clip is made
        if (st.DeviceVoice) return new DjCue(said.Text, said.Caption, null, true, DjIntro.Usable(extra?.Vibe?.Intro));
        // one voice only: the line in the saved voice; when that can't be had (over today's cap, Cloudflare failing) the planned line, or its pooled form, from
        // clips already made in that same voice; otherwise the DJ stays quiet. Never another voice, never the device's.
        var id = pooled ? clips.RequestPooled(said.Parts!, s.Id) ?? clips.Request(said.Text) : clips.Request(said.Text);
        if (id is null && !ReferenceEquals(said, planned) && clips.Cached(planned.Text) is { } whole) { id = whole; said = planned; }
        if (id is null && planned.Parts is { Count: > 0 } parts && planned.PooledCaption is { } pc && clips.CachedPooled(parts, s.Id) is { } kept)
        { id = kept; said = planned with { Text = DjPool.Join(parts), Caption = pc }; }
        if (id is null) return null;
        if (counted.TryAdd(key, true)) voice.CountSaid();
        // the song's intro, when known for sure, so the player can time the line to end before the singing
        return new DjCue(said.Text, said.Caption, DjClips.UrlOf(id), false, DjIntro.Usable(extra?.Vibe?.Intro));
    }

    /// <summary>A 25-word line in the saved voice: what the text model must leave room for under the cap.</summary>
    double ClipRoom() => CloudflareVoice.EstimateNeurons(voice.Current.Voice, string.Join(' ', Enumerable.Repeat("word", DjWriter.MaxWords)) + " and some.");

    /// <summary>
    /// How the planned line is said. With an Aura voice, a whole line (the model's, or the template) only while it leaves room for the pooled lines still to come
    /// today (<see cref="DjBudget"/>); otherwise the pooled form, whose phrases and name clip are kept and shared. A line whose clip is already made is whole for free.
    /// </summary>
    async Task<(DjLine Line, bool Pooled)> VoiceAsync(DjRequest req, DjLine line, DjMemory mem, DjStyle style, DjExtra? extra, string? firstName)
    {
        var c = voice.Current;
        var ai = AiLines && c.AiLines && voice.Ready;
        if (c.Voice != CloudflareVoice.MeloTts && voice.Ready && line.Parts is { Count: > 0 } parts && line.PooledCaption is { } pc)
        {
            var whole = CloudflareVoice.EstimateNeurons(c.Voice, ai ? new string('x', DjBudget.WholeChars) : line.Text)
                + (ai ? DjModels.Primary.Cost(DjBudget.PromptTokens, DjBudget.MaxTokens) : 0);
            if (!voice.WholeAllowed(whole, req.LocalHour) && !(!ai && File.Exists(clips.PathOf(line.Text))))
            {
                voice.NoteWrite("Keeping today's cap for more lines: a pooled line instead");
                var pooled = line with { Text = DjPool.Join(parts), Caption = pc };
                Remember(mem, line, pooled.Text, pc);
                return (pooled, true);
            }
        }
        return (ai ? await RewriteAsync(req, line, mem, extra, style, firstName) : line, false);
    }

    /// <summary>The line said replaces the planned one in the memory, so the next prompts avoid it.</summary>
    static void Remember(DjMemory mem, DjLine planned, string text, string caption)
    {
        lock (mem)
        {
            var mine = mem.Lines.LastOrDefault(l => l.Text == planned.Text && l.TemplateId == planned.TemplateId);
            var i = mine is null ? -1 : mem.Lines.IndexOf(mine);
            if (i >= 0) mem.Lines[i] = mine! with { Text = text, Written = caption };
        }
    }

    /// <summary>The planner's line reworded by the text model, or [line] itself when the model fails, is too slow, is near the cap or writes something doubtful.
    /// A line it wrote replaces the template line in the memory, so the next prompts avoid it.</summary>
    async Task<DjLine> RewriteAsync(DjRequest req, DjLine line, DjMemory mem, DjExtra? extra = null, DjStyle style = DjStyle.Classic, string? firstName = null)
    {
        try
        {
            List<string> recent;
            DjMemory.Line? mine;
            lock (mem)
            {
                mine = mem.Lines.LastOrDefault(l => l.Text == line.Text && l.TemplateId == line.TemplateId);
                recent = mem.Lines.Where(l => !ReferenceEquals(l, mine)).TakeLast(DjWriter.Recent).Select(l => l.Written ?? l.Text).ToList();
            }
            var f = DjWriter.FactsOf(req, line, clock(), extra, style, firstName);
            var (sys, user) = DjWriter.Prompt(f, recent);
            using var cts = new CancellationTokenSource(WriteTimeout);
            string? raw;
            try { raw = await voice.WriteAsync(sys, user, ClipRoom(), cts.Token).WaitAsync(WriteTimeout); }
            catch (TimeoutException) { cts.Cancel(); return line; }
            var caption = DjWriter.Check(raw, f, recent);
            if (caption is null)
            {
                if (raw is not null) log.LogInformation("DJ: the model's line was not used, a template line instead");
                return line;
            }
            var text = DjWriter.Speak(caption, f, Pronounce());
            if (text.Length == 0) return line;
            lock (mem)
            {
                var i = mine is null ? -1 : mem.Lines.IndexOf(mine);
                if (i >= 0) mem.Lines[i] = mine! with { Text = text, Written = caption };
            }
            return line with { Text = text, Caption = caption };
        }
        catch (Exception e) { log.LogWarning("DJ line not reworded: {Kind}", e.GetType().Name); return line; }
    }

    /// <summary>A song for the Test button's sample line.</summary>
    public static readonly DjRequest TestSong = new(DjSurface.Web, new DjSong("Smack That", "Akon, Eminem", "Konvicted", "Akon", 2006, "Hip-Hop", 212_000, "/test/smack-that.flac"),
        new DjSong("Hey Ya!", "Outkast", "Speakerboxxx/The Love Below", "Outkast", 2003, "Hip-Hop", 235_000, "/test/hey-ya.flac"), LocalHour: 20, Weekday: 5);

    /// <summary>
    /// The admin's Test button: in [voiceName] (null: the saved voice), a sample line written as the DJ writes them (the model when it is on, else a template)
    /// before <see cref="TestSong"/>, or <see cref="TestLine"/> when none can be written; made now, said as the DJ says it. Its address, or why it could not.
    /// </summary>
    public async Task<(string? Clip, string Message)> TestAsync(CancellationToken ct = default, string? voiceName = null)
    {
        var say = TestLineSaid(TestLine);
        if (AiLines && voice.Current.AiLines && voice.Ready)
        {
            var planned = DjPlanner.Plan(new DjSettings { Enabled = true }, TestSong with { Prev = TestSong.Prev }, new DjMemory(), clock(), new Random(7), Pronounce());
            if (planned is not null) say = (await RewriteAsync(TestSong, planned, new DjMemory())).Text;
        }
        var (id, msg) = await clips.MakeNowAsync(say, ct, voiceName);
        return (id is null ? null : DjClips.UrlOf(id), id is null ? msg : msg + ": \"" + say + "\"");
    }

    string TestLineSaid(string s) => Pronounce().Phrases(s);
}
