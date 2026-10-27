using System.Text.Json;
using FLACie.Core;
using FLACie.Server;
using FLACie.Server.Alexa;
using FLACie.Server.Dj;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FLACie.Tests;

/// <summary>
/// The one per-person context shared by "talk to the DJ" and the lines between songs: what is asked reaches the line writer, the songs the DJ sees reach the ask
/// prompt, lines keep a request going without repeating themselves, and each account has its own. Cloudflare is faked: nothing leaves the machine.
/// </summary>
public class DjContextTests : IDisposable
{
    const string Account = "0123456789abcdef0123456789abcdef";
    readonly string dir = Directory.CreateTempSubdirectory("flacie-dj-context").FullName;
    public void Dispose() { try { Directory.Delete(dir, true); } catch (Exception) { } }
    DateTime now = new(2026, 10, 25, 19, 0, 0, DateTimeKind.Utc);
    readonly UserSession me = new("jellyfin", new JellyfinAccount("http://jf.test", "u1", "tester", "tok"), null) { Id = "u1" };

    static readonly DjSong Prev = new("Hey Ya!", "Outkast", "Speakerboxxx/The Love Below", "Outkast", 2003, "Hip-Hop", 235_000, "/m/heyya.flac");
    static readonly DjSong Smack = new("Smack That", "Akon, Eminem", "Konvicted", "Akon", 2006, "Hip-Hop", 212_000, "/m/smack.flac");
    static DjRequest Req() => new(DjSurface.Auto, Smack, Prev, LocalHour: 20, Weekday: 5, Why: "Because you play Outkast");
    static readonly DjLine Template = new(DjSlot.Intro, "intro1", "Next, Smack That by Akon with Em-in-em.", "Next, Smack That by Akon with Eminem.");

    static HttpResponseMessage Model(string line) =>
        DjVoiceTests.Json(JsonSerializer.Serialize(new { result = new { response = line, usage = new { prompt_tokens = 400, completion_tokens = 30 } }, success = true, errors = Array.Empty<object>() }));

    (DjService Dj, DjVoiceTests.Handler Http) Make(DjContexts contexts, Func<HttpRequestMessage, Task<HttpResponseMessage>> model)
    {
        var paths = new DataPaths(dir); var dp = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(dir, "keys")));
        var h = new DjVoiceTests.Handler(r => r.RequestUri!.ToString().Contains("/@cf/meta/") ? model(r) : Task.FromResult(DjVoiceTests.Audio()));
        var v = new CloudflareVoice(paths, new DjVoiceTests.Factory(h), dp, new ConfigurationBuilder().Build(), new DjVoiceTests.Lines<CloudflareVoice>(), () => now);
        v.Save(new DjVoiceConfig { AccountId = Account, CapPercent = 50, Voice = CloudflareVoice.DefaultVoice, AiLines = true }, "cf_fake_token");
        var clips = new DjClips(paths, v, new DjVoiceTests.Lines<DjClips>(), () => now);
        var dj = new DjService(new UserStateStore(paths, dp), v, clips, paths, new DjVoiceTests.Lines<DjService>(), () => now) { AiLines = true, WriteTimeout = TimeSpan.FromMilliseconds(500), Contexts = contexts };
        var st = dj.Settings(me); st.Enabled = true; dj.Save(me, st);
        return (dj, h);
    }

    static List<string> Prompts(DjVoiceTests.Handler h)
    {
        lock (h.Seen) return h.Seen.Where(x => x.Url.Contains("/@cf/meta/")).Select(x => JsonDocument.Parse(x.Body).RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!).ToList();
    }

    sealed class User(string account) : IDjAskUser
    {
        public string Account => account;
        public bool Ready => true;
        public List<Track> Library = [];
        public List<Playlist> Lists = [];
        public IReadOnlyList<Track> Songs => Library;
        public IReadOnlyList<Playlist> Playlists => Lists;
        public Track? Find(string path) => Library.FirstOrDefault(t => t.Path == path);
        public Track? FindSong(string artist, string title) => null;
        public IReadOnlyList<Track> Favourites => [];
        public IReadOnlyList<Track> MostPlayed(int take) => [];
        public bool Heard(Track t) => false;
        public IReadOnlyList<Track> RecommendNext(IReadOnlyList<string> session, int n) => [];
        public Vibe? VibeOf(Track t) => null;
        public Task RateAsync(Track t, int stars) => Task.CompletedTask;
        public Task<AlexaRequested?> RequestDownloadAsync(string words, CancellationToken ct) => Task.FromResult<AlexaRequested?>(null);
    }

    sealed class Capture(string reply) : IDjAskModel
    {
        public List<string> Users = [];
        public Task<string?> AskAsync(string system, string user, CancellationToken ct) { Users.Add(user); return Task.FromResult<string?>(reply); }
    }

    static Track T(string title, string artist) => new($"/p/{artist}/{title}", title, artist, "A", artist, 1, 1, 200000, 2006, null, 1, TrackSource.Jellyfin, JellyfinId: Guid.NewGuid().ToString("N"));

    static User Lib(string account)
    {
        var u = new User(account);
        u.Library.AddRange([T("Smack That", "Akon"), T("Levels", "Avicii"), T("Hey Ya!", "Outkast")]);
        u.Lists.Add(new("pl-car", "Car songs", 1, null, [new(u.Library[1].Path, "Levels", "Avicii"), new(u.Library[0].Path, "Smack That", "Akon")]));
        return u;
    }

    [Fact]
    public async Task What_was_asked_reaches_the_line_writer_and_the_songs_heard_reach_the_ask_prompt()
    {
        var contexts = new DjContexts();
        var model = new Capture("{\"answer\": \"Not sure, to be honest.\"}");
        var engine = new DjAskEngine(model, contexts, () => now);
        var u = Lib(me.Id);
        var asked = await engine.AnswerAsync(u, "play my car songs playlist", null, []);
        Assert.Equal(DjAskDo.Play, asked.Do);

        // the line before the next song is written knowing what was asked, and the conversation so far
        var (dj, h) = Make(contexts, _ => Task.FromResult(Model("Keeping the Car songs going, here's Smack That, Akon with Eminem.")));
        var cue = await dj.PrepareAsync(me, Req());
        var linePrompt = Assert.Single(Prompts(h));
        Assert.Contains("They asked you for (you may keep it going, in your own words): Car songs", linePrompt);
        Assert.Contains("They said: play my car songs playlist", linePrompt);
        Assert.Contains("Listening on: the car", linePrompt);
        // the line may name the request (it is a fact now), and it is shown as written
        Assert.Equal("Keeping the Car songs going, here's Smack That, Akon with Eminem.", cue?.Caption);

        // and the ask prompt sees the songs the DJ saw, the request and the DJ's own last line
        await engine.AnswerAsync(u, "why did you pick this?", u.Library[0].Path, []);
        var askPrompt = model.Users[^1];
        Assert.Contains("Hey Ya! by Outkast", askPrompt);
        Assert.Contains("They asked for: Car songs", askPrompt);
        Assert.Contains("Keeping the Car songs going", askPrompt);
        Assert.Contains("Listening on: the car", askPrompt);
    }

    [Fact]
    public async Task A_line_never_repeats_one_the_DJ_said_lately()
    {
        var contexts = new DjContexts();
        var said = "Friday evening, here's Smack That, Akon with Eminem.";
        contexts.For(me.Id).Said(said);
        var (dj, h) = Make(contexts, _ => Task.FromResult(Model(said)));
        var cue = await dj.PrepareAsync(me, Req());
        Assert.Contains(said, Assert.Single(Prompts(h)));   // told to avoid it
        Assert.NotEqual(said, cue?.Caption);                // and refused when it comes back anyway (the template instead)
    }

    [Fact]
    public void The_request_is_a_fact_a_line_may_use_and_without_it_the_name_is_refused()
    {
        var f = DjWriter.FactsOf(Req(), Template, now);
        const string line = "Keeping the Car songs going: Smack That, Akon with Eminem.";
        Assert.Null(DjWriter.Check(line, f, []));
        Assert.Equal(line, DjWriter.Check(line, f with { Request = "Car songs" }, []));
        Assert.Null(DjWriter.Check(line, f with { Request = "Car songs" }, [line]));
        var (_, user) = DjWriter.Prompt(f with { Request = "Car songs", Context = "- Listening on: the car\n" }, []);
        Assert.Contains("Car songs", user); Assert.Contains("Listening on: the car", user);
    }

    [Fact]
    public async Task Each_account_has_its_own_context()
    {
        var contexts = new DjContexts();
        var engine = new DjAskEngine(new Capture("{\"answer\": \"Not sure.\"}"), contexts, () => now);
        await engine.AnswerAsync(Lib("ash"), "play my car songs playlist", null, []);
        Assert.Equal("Car songs", contexts.For("ash").Request(now));
        Assert.Null(contexts.For("sam").Request(now));
        Assert.Empty(contexts.For("sam").Conversation(now));
        Assert.DoesNotContain("car songs", contexts.For("sam").Summary(now), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_conversation_is_forgotten_after_ten_minutes_and_a_new_session_starts_after_thirty()
    {
        var c = new DjContext();
        c.Asked("who are the prodigy", "An electronic act.", now);
        c.Requested("something chill", now); c.About("The Prodigy", now);
        c.Played("/a", "A", "X", false, 0.2, now);
        Assert.Single(c.Conversation(now.AddMinutes(9)));
        Assert.Empty(c.Conversation(now.AddMinutes(11)));
        Assert.Null(c.Request(now.AddMinutes(11))); Assert.Null(c.Subject(now.AddMinutes(11)));
        Assert.DoesNotContain("prodigy", c.Summary(now.AddMinutes(11)), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Last songs", c.Summary(now.AddMinutes(11)));
        Assert.DoesNotContain("Last songs", c.Summary(now.AddMinutes(31)));
    }

    [Fact]
    public void The_summary_is_compact_and_grounded()
    {
        var c = new DjContext();
        for (var i = 0; i < 14; i++) c.Played("/s" + i, "Song " + i, "Artist", i == 13, 0.2 + i * 0.05, now.AddMinutes(i * 3));
        c.Taste = () => (["Akon", "Outkast", "Avicii", "Massive Attack", "Moby", "Sixth"], ["Smack That by Akon"]);
        var s = c.Summary(now.AddMinutes(40), 8);
        Assert.Contains("Time of day: morning", s);
        Assert.Contains("Listening for: 40 minutes", s);
        Assert.Contains("Song 13 by Artist (skipped)", s);
        Assert.DoesNotContain("Song 3 ", s);                       // only the last ten
        Assert.Contains("Energy: building up", s);
        Assert.DoesNotContain("Sixth", s);                         // five top artists at most
        Assert.True(s.Length < 900, s.Length.ToString());
    }
}
