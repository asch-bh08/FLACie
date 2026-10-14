using System.Text.Json;
using FLACie.Core;

namespace FLACie.Server.Alexa;

/// <summary>A song Alexa has been asked to download.</summary>
public sealed record AlexaRequested(string Artist, string Title, bool InLibrary = false);

/// <summary>One download as the voice side needs it: running, done (with the library path when the song can be played) or failed.</summary>
public sealed record AlexaDownload(string Artist, string Title, string State, string Message, DateTime At);

/// <summary>Everything the voice side needs from one person's FLACie: library, playlists, ratings and downloads. A fake in tests; <see cref="SessionAlexaUser"/> in the server.</summary>
public interface IAlexaUser
{
    string Name { get; }
    /// <summary>False while the library is still loading (or Jellyfin cannot be reached): a song that is "not found" then means nothing, so nothing is searched or downloaded.</summary>
    bool Ready { get; }
    string? Problem { get; }
    IReadOnlyList<Track> Songs { get; }
    IReadOnlyList<Playlist> Playlists { get; }
    Track? Find(string path);
    /// <summary>The library song that is the same as this artist and title (even if the downloaded copy is not scanned yet it is null).</summary>
    Track? FindSong(string artist, string title);
    Task RateAsync(Track t, int stars);
    UserSession.PlaylistAdd AddToPlaylist(string playlistName, Track t, out string playlistUsed);
    /// <summary>Songs that sound like this artist's (same genres), best first.</summary>
    IReadOnlyList<Track> SimilarTo(FLACie.Core.Group artist, int take);
    /// <summary>Looks the words up in the public catalog and starts downloading the best match; null when nothing matches.</summary>
    Task<AlexaRequested?> RequestDownloadAsync(string words, CancellationToken ct);
    IReadOnlyList<AlexaDownload> Downloads();
}

/// <summary>An AudioPlayer.Play: which song, from where, and the token that says which place in which queue it is.</summary>
public sealed record AlexaTrackPlay(Track Track, long OffsetMs, string Token, string? ExpectedPrevious = null);

/// <summary>What the skill says and does in answer to one request.</summary>
public sealed record Reply(string Speech, AlexaTrackPlay? Play = null, bool Stop = false, bool EndSession = true, string? Reprompt = null, bool ClearQueue = false, bool AskNotifyPermission = false)
{
    public static Reply Say(string s, bool end = true) => new(s, EndSession: end, Reprompt: end ? null : s);
}

/// <summary>
/// The voice logic, with no HTTP and no Alexa JSON in it: play something (or fetch it), keep the queue, thumbs, playlists, "what is downloading". One call per request.
/// Replies are short; long waits use <paramref name="progress"/> ("Searching...") which the skill turns into a progressive response.
/// </summary>
public sealed class AlexaEngine(AlexaQueues queues, Func<AlexaConfig> config, Func<DateTime>? clock = null)
{
    readonly Func<DateTime> now = clock ?? (() => DateTime.UtcNow);
    static readonly TimeSpan ChoiceLife = TimeSpan.FromMinutes(2);
    readonly Dictionary<IAlexaUser, (IReadOnlyList<Track> Songs, IReadOnlyList<Playlist> Lists, VoiceMatcher M)> matchers = new(ReferenceEqualityComparer.Instance!);
    readonly object gate = new();

    /// <summary>
    /// The index of a person's library. It is rebuilt when the library (or a playlist) changes, but never while someone waits for an answer: the old index answers until the new one is
    /// ready (a 10,000 song build takes a second or two). Only the very first build is waited for.
    /// </summary>
    VoiceMatcher MatcherFor(IAlexaUser u)
    {
        lock (gate)
        {
            var songs = u.Songs; var lists = u.Playlists;
            if (matchers.TryGetValue(u, out var m))
            {
                if (ReferenceEquals(m.Songs, songs) && ReferenceEquals(m.Lists, lists)) return m.M;
                if (!building.Add(u)) return m.M;
                _ = Task.Run(() => { var fresh = new VoiceMatcher(songs, lists); lock (gate) { matchers[u] = (songs, lists, fresh); building.Remove(u); } });
                return m.M;
            }
            var built = new VoiceMatcher(songs, lists); matchers[u] = (songs, lists, built); return built;
        }
    }
    readonly HashSet<IAlexaUser> building = new(ReferenceEqualityComparer.Instance!);

    public static string Token(AlexaQueue q, int index, int pass) => $"{q.Serial}:{index}:{pass}";
    static bool TryToken(string? token, out long serial, out int index, out int pass)
    {
        serial = 0; index = 0; pass = 0;
        var p = (token ?? "").Split(':');
        return p.Length == 3 && long.TryParse(p[0], out serial) && int.TryParse(p[1], out index) && int.TryParse(p[2], out pass);
    }

    AlexaTrackPlay? PlayCurrent(IAlexaUser u, AlexaQueue q, long offset)
    {
        for (var guard = 0; guard < 50 && q.Current is { } path; guard++)
        {
            if (u.Find(path) is { JellyfinId.Length: > 0 } t) { q.Pass = 0; return new(t, offset, Token(q, q.Index, q.Pass)); }
            q.Remove(path);   // left the library, or never had a Jellyfin id to stream from
            offset = 0;
        }
        return null;
    }

    // ---- playing something ----

    public async Task<Reply> PlayAsync(IAlexaUser u, string linkId, VoiceQuery query, Func<string, Task>? progress = null, CancellationToken ct = default)
    {
        static string? Cap(string? x) => x is { Length: > 200 } ? x[..200] : x;   // what is heard is never this long; the limit keeps a flood of text from costing anything
        query = query with { Title = Cap(query.Title), Artist = Cap(query.Artist), Album = Cap(query.Album), Playlist = Cap(query.Playlist), Raw = Cap(query.Raw), SimilarTo = Cap(query.SimilarTo) };
        var q = queues.For(linkId);
        if (!u.Ready) return Reply.Say(u.Problem is { Length: > 0 } ? "I can't reach your music library right now." : "I'm still loading your library. Try again in a moment.");
        var m = MatcherFor(u).Match(query);
        lock (q) q.Pending = null;
        switch (m.Decision)
        {
            case Decision.Confident: return Start(u, linkId, q, m.Ranked[0], query);
            case Decision.Ambiguous:
                lock (q) { q.Pending = new() { Query = JsonSerializer.Serialize(query), Count = Math.Min(2, m.Ranked.Count), At = now() }; }
                queues.Save(linkId);
                return Reply.Say(MatcherFor(u).DisambiguationQuestion(m), end: false);
            default: return await NotFoundAsync(u, linkId, q, query, progress, ct);
        }
    }

    /// <summary>"The first one", "the second", "yes", "no": answers the question asked in the last 2 minutes.</summary>
    public async Task<Reply> ChooseAsync(IAlexaUser u, string linkId, int index, Func<string, Task>? progress = null, CancellationToken ct = default)
    {
        var q = queues.For(linkId);
        Pending? p; lock (q) p = q.Pending;
        if (p is null || now() - p.At > ChoiceLife) return Reply.Say("I didn't ask anything. What would you like to hear?", end: false);
        var query = JsonSerializer.Deserialize<VoiceQuery>(p.Query)!;
        lock (q) q.Pending = null;
        if (index < 0) return Reply.Say("Okay. What would you like to hear?", end: false);
        var m = MatcherFor(u).Match(query);
        if (m.Ranked.Count <= index) return await PlayAsync(u, linkId, query, progress, ct);
        return Start(u, linkId, q, m.Ranked[index], query);
    }

    Reply Start(IAlexaUser u, string linkId, AlexaQueue q, VoiceMatch match, VoiceQuery query)
    {
        List<string> paths; var start = 0; bool? shuffle = null; string say; string title;
        switch (match.Item)
        {
            case Track { JellyfinId: null } nt:
                return Reply.Say($"{Phonetic.SpeechSafe(Phonetic.CleanTitle(nt.Title))} is on its way into the library. Jellyfin hasn't added it yet.");
            case Track t:
                var others = u.Songs.Where(x => x.Path != t.Path && x.JellyfinId is not null && Matching.PrimaryArtist(x.Artist) == Matching.PrimaryArtist(t.Artist)).OrderBy(_ => Random.Shared.Next()).Take(25).Select(x => x.Path);
                paths = [t.Path, .. others]; shuffle = false; say = $"Playing {Phonetic.SpeechSafe(Phonetic.CleanTitle(t.Title))} by {Phonetic.SpeechSafe(t.Artist)}."; title = t.Title; break;
            case FLACie.Core.Group g when match.Kind == MatchKind.Artist:
                var songs = g.Tracks.Where(x => x.JellyfinId is not null).ToList();
                if (query.SimilarTo is not null)
                {
                    var like = u.SimilarTo(g, 60).Where(x => x.JellyfinId is not null).ToList();
                    songs = [.. songs.OrderBy(_ => Random.Shared.Next()).Take(8), .. like];
                    songs = songs.DistinctBy(x => x.Path).ToList(); say = $"Playing music like {Phonetic.SpeechSafe(g.Name)}."; shuffle = false;
                }
                else { say = $"Playing {Phonetic.SpeechSafe(g.Name)}."; shuffle = true; }
                paths = songs.Take(300).Select(x => x.Path).ToList(); title = g.Name; break;
            case FLACie.Core.Group a:
                paths = a.Tracks.Where(x => x.JellyfinId is not null).OrderBy(x => x.DiscNo).ThenBy(x => x.TrackNo).Select(x => x.Path).ToList(); shuffle = q.Shuffle; say = $"Playing the album {Phonetic.SpeechSafe(a.Name)}."; title = a.Name; break;
            case Playlist p:
                paths = p.Entries.Select(e => u.Find(e.Path)?.Path ?? u.Songs.FirstOrDefault(x => Matching.MatchKey(x.Title, x.Artist) == Matching.MatchKey(e.Title, e.Artist))?.Path).Where(x => x is not null && u.Find(x)?.JellyfinId is not null).Cast<string>().Distinct().Take(500).ToList();
                var made = p.Id.StartsWith("voice-", StringComparison.Ordinal);
                say = made ? $"Playing {Phonetic.SpeechSafe(match.Spoken)}." : $"Playing your {Phonetic.SpeechSafe(p.Name)} playlist."; title = p.Name;
                if (made) shuffle = true; break;
            default: return Reply.Say("Sorry, I couldn't play that.");
        }
        if (paths.Count == 0) return Reply.Say($"I found {Phonetic.SpeechSafe(title)}, but none of it can be played yet.");
        lock (q) { q.Set(paths, start, shuffle, title); q.Pending = null; }
        var play = PlayCurrent(u, q, 0);
        queues.Save(linkId);
        return play is null ? Reply.Say("Sorry, I couldn't play that.") : new(say, play);
    }

    async Task<Reply> NotFoundAsync(IAlexaUser u, string linkId, AlexaQueue q, VoiceQuery query, Func<string, Task>? progress, CancellationToken ct)
    {
        var words = query.Title is { Length: > 0 } t ? (query.Artist is { Length: > 0 } a ? $"{t} {a}" : t) : query.Album is { Length: > 0 } al ? (query.Artist is { Length: > 0 } a2 ? $"{al} {a2}" : al) : query.Raw ?? query.Artist ?? "";
        if (query.Playlist is not null) return Reply.Say($"I don't have a playlist called {Phonetic.SpeechSafe(query.Playlist)}.");
        if (query.Decade is not null || query.Genre is not null || query.SimilarTo is not null || words.Length < 2) return Reply.Say("Sorry, I couldn't find that in your library.");
        if (!config().Downloads) return Reply.Say($"I don't have {Phonetic.SpeechSafe(words)} in your library, and downloads from Alexa are turned off.");
        if (progress is not null) { try { await progress("I don't have that. Searching and downloading it now."); } catch (Exception) { } }
        AlexaRequested? req;
        try { req = await u.RequestDownloadAsync(words, ct); } catch (OperationCanceledException) { throw; } catch (Exception) { req = null; }
        if (req is null) return Reply.Say($"I don't have {Phonetic.SpeechSafe(words)}, and I couldn't find it to download.");
        if (req.InLibrary && u.FindSong(req.Artist, req.Title) is { JellyfinId.Length: > 0 } have)
            return Start(u, linkId, q, new VoiceMatch(MatchKind.Track, have, 1.0, ""), query);
        lock (q) { q.PendingTitle = $"{req.Title} by {req.Artist}"; q.LastDownloadKey = Matching.MatchKey(req.Title, req.Artist); q.LastDownloadAt = null; }
        queues.Save(linkId);
        return Reply.Say($"I'm downloading {Phonetic.SpeechSafe(req.Title)} by {Phonetic.SpeechSafe(req.Artist)} now. I'll tell you when it's ready.", end: true) with { AskNotifyPermission = true };
    }

    /// <summary>"Play it", "play the last download": the song most recently asked for through Alexa, if it has arrived in the library.</summary>
    public Reply PlayLastDownload(IAlexaUser u, string linkId)
    {
        var q = queues.For(linkId);
        if (q.LastDownloadKey is not { } key) return Reply.Say("You haven't asked me to download anything lately.");
        var done = u.Downloads().FirstOrDefault(d => Matching.MatchKey(d.Title, d.Artist) == key);
        var song = u.Songs.FirstOrDefault(s => Matching.MatchKey(s) == key && s.JellyfinId is not null);
        if (song is null)
            return done?.State switch
            {
                "failed" => Reply.Say($"I couldn't download {Phonetic.SpeechSafe(q.PendingTitle ?? "it")}. {Phonetic.SpeechSafe(done.Message)}"),
                "done" => Reply.Say("It's downloaded. Jellyfin is still adding it to the library. Ask me again in a minute."),
                _ => Reply.Say("It's not ready yet. I'm still downloading it."),
            };
        lock (q) { q.Set([song.Path], 0, false, song.Title); }
        queues.Save(linkId);
        return PlayCurrent(u, q, 0) is { } play ? new($"Playing {Phonetic.SpeechSafe(song.Title)} by {Phonetic.SpeechSafe(song.Artist)}.", play) : Reply.Say("Sorry, I couldn't play that.");
    }

    public Reply IsItReady(IAlexaUser u, string linkId)
    {
        var q = queues.For(linkId);
        if (q.LastDownloadKey is not { } key) return Reply.Say("You haven't asked me to download anything lately.");
        var what = Phonetic.SpeechSafe(q.PendingTitle ?? "that song");
        if (u.Songs.Any(s => Matching.MatchKey(s) == key && s.JellyfinId is not null)) return Reply.Say($"Yes, {what} is ready. Say play the last download.");
        var d = u.Downloads().FirstOrDefault(x => Matching.MatchKey(x.Title, x.Artist) == key);
        return d?.State switch
        {
            "failed" => Reply.Say($"I couldn't get {what}. {Phonetic.SpeechSafe(d.Message)}"),
            "done" => Reply.Say($"{what} is downloaded. Jellyfin is still adding it."),
            "running" => Reply.Say($"Not yet. I'm still downloading {what}. {Phonetic.SpeechSafe(d.Message)}"),
            _ => Reply.Say("I'm not downloading anything right now."),
        };
    }

    public Reply WhatsDownloading(IAlexaUser u)
    {
        var running = u.Downloads().Where(d => d.State == "running").ToList();
        if (running.Count == 0)
        {
            var last = u.Downloads().FirstOrDefault();
            return Reply.Say(last is null ? "Nothing is downloading." : $"Nothing is downloading. The last one was {Phonetic.SpeechSafe(last.Title)} by {Phonetic.SpeechSafe(last.Artist)}, and it {(last.State == "done" ? "finished" : "failed")}.");
        }
        var names = string.Join(", ", running.Take(3).Select(d => $"{Phonetic.SpeechSafe(d.Title)} by {Phonetic.SpeechSafe(d.Artist)}"));
        return Reply.Say(running.Count == 1 ? $"I'm downloading {names}." : $"I'm downloading {running.Count} songs, including {names}.");
    }

    // ---- shortcuts ----

    public Reply PlayRecent(IAlexaUser u, string linkId) => PlayList(u, linkId, u.Songs.Where(s => s.JellyfinId is not null).OrderByDescending(s => s.AddedMs).Take(50).ToList(), "your recently added music", false);
    public Reply PlayLibrary(IAlexaUser u, string linkId) => PlayList(u, linkId, u.Songs.Where(s => s.JellyfinId is not null).OrderBy(_ => Random.Shared.Next()).Take(500).ToList(), "your library", true);

    Reply PlayList(IAlexaUser u, string linkId, List<Track> tracks, string what, bool shuffle)
    {
        if (tracks.Count == 0) return Reply.Say("There's nothing to play yet.");
        var q = queues.For(linkId);
        lock (q) q.Set(tracks.Select(t => t.Path).ToList(), 0, shuffle ? false : null, what);
        queues.Save(linkId);
        return PlayCurrent(u, q, 0) is { } play ? new($"Playing {what}.", play) : Reply.Say("Sorry, I couldn't play that.");
    }

    // ---- controls ----

    /// <summary>Remembers where the player is, from the AudioPlayer part of any request.</summary>
    public void Position(string linkId, string? token, long offsetMs)
    {
        var q = queues.For(linkId);
        if (TryToken(token, out var serial, out var idx, out _) && serial == q.Serial && idx == q.Index) { q.OffsetMs = Math.Max(0, offsetMs); }
    }

    public Reply Next(IAlexaUser u, string linkId)
    {
        var q = queues.For(linkId);
        if (q.Empty) return Reply.Say("Nothing is playing. What would you like to hear?", end: false);
        bool moved; lock (q) moved = q.Next(true);
        if (!moved) { lock (q) { q.Playing = false; q.OffsetMs = 0; } queues.Save(linkId); return new("That was the last song.", Stop: true); }
        queues.Save(linkId);
        return PlayCurrent(u, q, 0) is { } p ? new("", p) : new("That was the last song.", Stop: true);
    }

    public Reply Previous(IAlexaUser u, string linkId)
    {
        var q = queues.For(linkId);
        if (q.Empty) return Reply.Say("Nothing is playing. What would you like to hear?", end: false);
        lock (q) q.Previous();
        queues.Save(linkId);
        return PlayCurrent(u, q, 0) is { } p ? new("", p) : Reply.Say("Sorry, I couldn't go back.");
    }

    public Reply Pause(string linkId)
    {
        var q = queues.For(linkId); lock (q) q.Playing = false; queues.Save(linkId);
        return new("", Stop: true);
    }

    public Reply Resume(IAlexaUser u, string linkId)
    {
        var q = queues.For(linkId);
        if (q.Empty) return Reply.Say("There's nothing to resume. What would you like to hear?", end: false);
        lock (q) { q.Serial++; q.Playing = true; }
        queues.Save(linkId);
        return PlayCurrent(u, q, q.OffsetMs) is { } p ? new("", p) : Reply.Say("Sorry, I couldn't resume that.");
    }

    public Reply StartOver(IAlexaUser u, string linkId)
    {
        var q = queues.For(linkId);
        if (q.Empty) return Reply.Say("Nothing is playing.");
        lock (q) { q.OffsetMs = 0; q.Serial++; q.Playing = true; }
        return PlayCurrent(u, q, 0) is { } p ? new("", p) : Reply.Say("Sorry, I couldn't do that.");
    }

    public Reply SetShuffle(IAlexaUser u, string linkId, bool on)
    {
        var q = queues.For(linkId); lock (q) q.SetShuffle(on); queues.Save(linkId);
        return Reply.Say(on ? "Shuffle is on." : "Shuffle is off.");
    }

    public Reply SetRepeat(string linkId, AlexaRepeat mode)
    {
        var q = queues.For(linkId); lock (q) q.Repeat = mode; queues.Save(linkId);
        return Reply.Say(mode switch { AlexaRepeat.Off => "Repeat is off.", AlexaRepeat.One => "Repeating this song.", _ => "Repeating the list." });
    }

    public Reply WhatsPlaying(IAlexaUser u, string linkId)
    {
        var q = queues.For(linkId);
        var t = q.Current is { } p ? u.Find(p) : null;
        return t is null ? Reply.Say("Nothing is playing.") : Reply.Say($"This is {Phonetic.SpeechSafe(Phonetic.CleanTitle(t.Title))} by {Phonetic.SpeechSafe(t.Artist)}{(t.Album.Length > 0 ? $", from {Phonetic.SpeechSafe(t.Album)}" : "")}.");
    }

    public async Task<Reply> RateAsync(IAlexaUser u, string linkId, bool like)
    {
        var q = queues.For(linkId);
        var t = q.Current is { } p ? u.Find(p) : null;
        if (t is null) return Reply.Say("Nothing is playing.");
        try { await u.RateAsync(t, like ? 5 : 1); } catch (Exception) { return Reply.Say("Sorry, I couldn't save that."); }
        if (like) return Reply.Say("Okay, I'll remember you like this.");
        // a dislike also skips
        var skip = Next(u, linkId);
        return skip with { Speech = "Okay, I won't play that as much." };
    }

    public Reply AddToPlaylist(IAlexaUser u, string linkId, string? playlistName)
    {
        var q = queues.For(linkId);
        var t = q.Current is { } p ? u.Find(p) : null;
        if (t is null) return Reply.Say("Nothing is playing.");
        if (string.IsNullOrWhiteSpace(playlistName)) return Reply.Say("Which playlist?", end: false);
        var r = u.AddToPlaylist(playlistName.Trim(), t, out var used);
        return r switch
        {
            UserSession.PlaylistAdd.Added => Reply.Say($"Added {Phonetic.SpeechSafe(Phonetic.CleanTitle(t.Title))} to {Phonetic.SpeechSafe(used)}."),
            UserSession.PlaylistAdd.Already => Reply.Say($"It's already in {Phonetic.SpeechSafe(used)}."),
            _ => Reply.Say($"I couldn't find a playlist called {Phonetic.SpeechSafe(playlistName)}."),
        };
    }

    // ---- AudioPlayer events ----

    /// <summary>PlaybackStarted: the token says which place in the list is now playing.</summary>
    public void Started(string linkId, string? token)
    {
        var q = queues.For(linkId);
        if (!TryToken(token, out var serial, out var idx, out var pass) || serial != q.Serial || idx < 0 || idx >= q.Order.Count) return;
        lock (q) { q.Index = idx; q.Pass = pass; q.Playing = true; q.OffsetMs = 0; q.FailStreak = 0; }
        queues.Save(linkId);
    }

    public void Stopped(string linkId, string? token, long offsetMs)
    {
        var q = queues.For(linkId);
        if (!TryToken(token, out var serial, out var idx, out _) || serial != q.Serial) return;
        lock (q) { q.Playing = false; if (idx == q.Index) q.OffsetMs = Math.Max(0, offsetMs); }
        queues.Save(linkId);
    }

    public void Finished(string linkId, string? token)
    {
        var q = queues.For(linkId);
        if (!TryToken(token, out var serial, out var idx, out _) || serial != q.Serial) return;
        lock (q) { if (q.NextIndex(false) is null && idx == q.Index) { q.Playing = false; q.OffsetMs = 0; } }
        queues.Save(linkId);
    }

    /// <summary>PlaybackNearlyFinished: line up the next song (gapless).</summary>
    public AlexaTrackPlay? NearlyFinished(IAlexaUser u, string linkId, string? token)
    {
        var q = queues.For(linkId);
        if (!TryToken(token, out var serial, out var idx, out var pass) || serial != q.Serial) return null;
        lock (q)
        {
            for (var tries = 0; tries < 5; tries++)
            {
                if (q.NextIndex(false) is not { } ni) return null;
                var np = ni <= idx ? pass + 1 : pass;
                if (u.Find(q.Order[ni]) is { JellyfinId.Length: > 0 } t) return new(t, 0, Token(q, ni, np), token);
                q.Remove(q.Order[ni]);   // gone: look at the next one
            }
            return null;
        }
    }

    /// <summary>PlaybackFailed: move to the next song, but give up after 3 failures in a row.</summary>
    public AlexaTrackPlay? Failed(IAlexaUser u, string linkId, string? token)
    {
        var q = queues.For(linkId);
        if (!TryToken(token, out var serial, out _, out _) || serial != q.Serial) return null;
        bool moved; lock (q) { q.FailStreak++; moved = q.FailStreak <= 3 && q.Next(true); }
        queues.Save(linkId);
        return moved ? PlayCurrent(u, q, 0) : null;
    }

    // ---- notices ----

    /// <summary>True the first time only: the permission card for notifications is shown once per link.</summary>
    public bool ShouldAskPermission(string linkId)
    {
        var q = queues.For(linkId);
        lock (q) { if (q.AskedNotify) return false; q.AskedNotify = true; }
        queues.Save(linkId);
        return true;
    }


    /// <summary>A download Alexa asked for has ended: remember what to tell the person (the next time they talk to FLACie, and as a notification).</summary>
    public void DownloadEnded(string linkId, string artist, string title, bool ok, string message)
    {
        var q = queues.For(linkId);
        lock (q)
        {
            if (q.LastDownloadKey == Matching.MatchKey(title, artist)) q.LastDownloadAt = now();
            q.Notice = ok ? $"{title} by {artist} is ready. Say, ask FLACie to play the last download." : $"I couldn't download {title} by {artist}.";
        }
        queues.Save(linkId);
    }

    /// <summary>The notice waiting for this person, once (it is cleared when read).</summary>
    public string? TakeNotice(string linkId)
    {
        var q = queues.For(linkId);
        string? n; lock (q) { n = q.Notice; q.Notice = null; }
        if (n is not null) queues.Save(linkId);
        return n;
    }
}
