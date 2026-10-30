using System.Net;
using System.Text.Json;
using FLACie.Core;
using FLACie.Server;
using FLACie.Server.Dj;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FLACie.Tests;

/// <summary>The DJ's second round: the 70B model with 8B behind it, more facts (MusicBrainz, the song's feel, loved or new), the hello, segues, time-of-day
/// colour, the Style setting, pooled lines with the clip cache, and the budget. Cloudflare is faked: nothing leaves the machine.</summary>
public class DjRoundTwoTests : IDisposable
{
    const string Account = "0123456789abcdef0123456789abcdef";
    readonly string dir = Directory.CreateTempSubdirectory("flacie-dj-round2").FullName;
    public void Dispose() { try { Directory.Delete(dir, true); } catch (Exception) { } }
    DateTime now = new(2026, 10, 25, 19, 0, 0, DateTimeKind.Utc);
    static readonly DateTime T0 = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

    static UserSession Person(string id, string name) => new("jellyfin", new JellyfinAccount("http://jf.test", id, name, "tok-" + id), null) { Id = id };
    readonly UserSession me = Person("u1", "ashley");

    static readonly DjSong Prev = new("Hey Ya!", "Outkast", "Speakerboxxx/The Love Below", "Outkast", 2003, "Hip-Hop", 235_000, "/m/heyya.flac");
    static readonly DjSong Smack = new("Smack That", "Akon, Eminem", "Konvicted", "Akon", 2006, "Hip-Hop", 212_000, "/m/smack.flac");
    static DjRequest Req(int hour = 20, DjBond bond = DjBond.Unknown, string? why = null) => new(DjSurface.Web, Smack, Prev, LocalHour: hour, Weekday: 5, Why: why, Bond: bond);
    static readonly DjLine Template = new(DjSlot.Intro, "intro1", "Next, Smack That by Akon with Em-in-em.", "Next, Smack That by Akon with Eminem.");

    // owner, 28 Oct: after a skip in their playlist the DJ said "skipped that last one, playing this one" as if it had chosen the song; the writer is now
    // told where the song comes from, and only an engine pick may be spoken of as the DJ's choice or a reaction to skips
    [Fact]
    public void A_song_from_their_playlist_is_theirs_and_the_skip_is_not_mentioned()
    {
        var pl = DjWriter.Prompt(DjWriter.FactsOf(Req() with { PlaylistId = "pl-1", PrevPlayedOut = false, SkippedRecently = true }, Template, now), []);
        Assert.Contains("their own playlist", pl.User);
        Assert.DoesNotContain("skipped", pl.User);
        var picked = DjWriter.Prompt(DjWriter.FactsOf(Req(why: "because you play Akon") with { PrevPlayedOut = false, SkippedRecently = true }, Template, now), []);
        Assert.Contains("you picked", picked.User);
        Assert.Contains("skipped", picked.User);
        Assert.Contains("Never say or suggest you chose", pl.System);
    }
    static readonly Vibe Big = new(0.85, 0.9, 120, 0.8), Calm = new(0.3, 0.4, 80, 0.3);

    static HttpResponseMessage Model(string line, int pin = 500, int pout = 30) =>
        DjVoiceTests.Json(JsonSerializer.Serialize(new { result = new { response = line, usage = new { prompt_tokens = pin, completion_tokens = pout } }, success = true, errors = Array.Empty<object>() }));

    sealed record Rig(DjService Dj, CloudflareVoice Voice, DjClips Clips, DjVoiceTests.Handler Http);

    Rig Make(Func<HttpRequestMessage, Task<HttpResponseMessage>>? model = null, int cap = 50, string voice = CloudflareVoice.DefaultVoice, bool ai = true,
        DjStyle style = DjStyle.Classic, string sub = "a", params UserSession[] people)
    {
        var paths = new DataPaths(Path.Combine(dir, sub)); var dp = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(dir, "keys")));
        model ??= _ => Task.FromResult(Model("Smack That, Akon with Eminem."));
        var h = new DjVoiceTests.Handler(r => r.RequestUri!.ToString().Contains("/@cf/meta/") ? model(r) : Task.FromResult(DjVoiceTests.Audio()));
        var v = new CloudflareVoice(paths, new DjVoiceTests.Factory(h), dp, new ConfigurationBuilder().Build(), new DjVoiceTests.Lines<CloudflareVoice>(), () => now);
        v.Save(new DjVoiceConfig { AccountId = Account, CapPercent = cap, Voice = voice, AiLines = ai }, "cf_fake_token");
        var clips = new DjClips(paths, v, new DjVoiceTests.Lines<DjClips>(), () => now);
        var dj = new DjService(new UserStateStore(paths, dp), v, clips, paths, new DjVoiceTests.Lines<DjService>(), () => now) { AiLines = true, WriteTimeout = TimeSpan.FromMilliseconds(500) };
        foreach (var p in people.Length == 0 ? [me] : people) { var st = dj.Settings(p); st.Enabled = true; st.Style = style; dj.Save(p, st); }
        return new(dj, v, clips, h);
    }

    static List<string> Calls(Rig r, string model) { lock (r.Http.Seen) return r.Http.Seen.Where(x => x.Url.EndsWith("/ai/run/" + model)).Select(x => x.Body).ToList(); }
    static List<string> Spoken(Rig r) { lock (r.Http.Seen) return r.Http.Seen.Where(x => x.Url.Contains("/deepgram/")).Select(x => JsonDocument.Parse(x.Body).RootElement.GetProperty("text").GetString()!).ToList(); }
    static string UserPrompt(string body) => JsonDocument.Parse(body).RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
    static string SystemPrompt(string body) => JsonDocument.Parse(body).RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
    static string IdOf(string url) => url["/api/dj/clip/".Length..^".mp3".Length];

    // ---- the stronger model, with 8B and then the templates behind it ----

    [Fact]
    public async Task The_70B_model_writes_the_line_and_is_the_only_call_when_it_answers()
    {
        Assert.Equal("@cf/meta/llama-3.3-70b-instruct-fp8-fast", CloudflareVoice.TextModel);
        Assert.Equal("@cf/meta/llama-3.1-8b-instruct", CloudflareVoice.FallbackModel);
        var r = Make(_ => Task.FromResult(Model("Friday night, so here's Smack That, Akon with Eminem.")));
        var cue = await r.Dj.PrepareAsync(me, Req());
        Assert.Equal("Friday night, so here's Smack That, Akon with Eminem.", cue!.Caption);
        Assert.Single(Calls(r, CloudflareVoice.TextModel));
        Assert.Empty(Calls(r, CloudflareVoice.FallbackModel));
        Assert.Equal(CloudflareVoice.TextModel, r.Voice.LastModel);
        // 500 tokens in, 30 out at the 70B price
        Assert.InRange(r.Voice.Usage.Neurons, DjModels.Primary.Cost(500, 30) - 0.001, DjModels.Primary.Cost(500, 30) + CloudflareVoice.EstimateNeurons(CloudflareVoice.DefaultVoice, cue.Text) + 0.001);
    }

    [Fact]
    public async Task When_70B_cannot_answer_8B_writes_it_and_when_neither_can_the_template_is_said()
    {
        var r = Make(q => Task.FromResult(q.RequestUri!.ToString().Contains("70b")
            ? DjVoiceTests.Json("{\"success\":false,\"errors\":[{\"message\":\"capacity\"}]}", HttpStatusCode.ServiceUnavailable)
            : Model("Smack That now, Akon with Eminem.")));
        var cue = await r.Dj.PrepareAsync(me, Req());
        Assert.Equal("Smack That now, Akon with Eminem.", cue!.Caption);
        Assert.Single(Calls(r, CloudflareVoice.TextModel)); Assert.Single(Calls(r, CloudflareVoice.FallbackModel));
        Assert.Equal(CloudflareVoice.FallbackModel, r.Voice.LastModel);

        var both = Make(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)), sub: "b");
        var t = await both.Dj.PrepareAsync(me, Req());
        Assert.Contains("Smack That", t!.Caption); Assert.Contains("Em-in-em", t.Text);   // the template line, respelt
        Assert.Single(Calls(both, CloudflareVoice.TextModel)); Assert.Single(Calls(both, CloudflareVoice.FallbackModel));   // one try each, no retries
    }

    [Fact]
    public async Task A_doubtful_line_or_a_refused_token_is_never_asked_of_8B_too()
    {
        var r = Make(_ => Task.FromResult(Model("Smack That won a Grammy in Germany, Akon with Eminem.")));
        var cue = await r.Dj.PrepareAsync(me, Req());
        Assert.DoesNotContain("Grammy", cue!.Caption);
        Assert.Single(Calls(r, CloudflareVoice.TextModel)); Assert.Empty(Calls(r, CloudflareVoice.FallbackModel));   // one paid call a line

        var no = Make(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)), sub: "b");
        await no.Dj.PrepareAsync(me, Req());
        Assert.Single(Calls(no, CloudflareVoice.TextModel)); Assert.Empty(Calls(no, CloudflareVoice.FallbackModel));
    }

    // ---- more facts, only certain ones ----

    [Fact]
    public void The_prompt_has_MusicBrainz_the_feel_and_loved_or_new_when_known_and_leaves_them_out_when_not()
    {
        var extra = new DjExtra("single", 2005, "", Big, Calm);
        var f = DjWriter.FactsOf(Req(bond: DjBond.Loved) with { Next = Smack with { Genre = "Hip Hop" } }, Template, now, extra);
        var (_, user) = DjWriter.Prompt(f, []);
        foreach (var fact in new[] { "Released as: a single", "First released: 2005", "Genre: hip hop", "How it sounds: a livelier one", "This song is: a song they love",
                     "Worth a word about the time: Friday night" })
            Assert.True(user.Contains(fact, StringComparison.OrdinalIgnoreCase), fact + " in " + user);
        Assert.DoesNotContain("Label", user);   // not known: never mentioned
        Assert.Contains("a new find", DjWriter.Prompt(DjWriter.FactsOf(Req(bond: DjBond.NewFind), Template, now), []).User);
        // the model may use them; they are respelt and said in words
        Assert.Equal("First out in 2005 as a single: Smack That, Akon with Eminem.", DjWriter.Check("First out in 2005 as a single: Smack That, Akon with Eminem.", f, []));
        Assert.Contains("two thousand and five", DjWriter.Speak("First out in 2005 as a single: Smack That, Akon with Eminem.", f, DjPronounce.Default));

        // nothing known: none of them, and "New to you" from the recommender is not taken as a fact any more
        var bare = DjWriter.Prompt(DjWriter.FactsOf(Req(why: "New to you") with { Next = Smack with { Genre = "" } }, Template, now), []).User;
        foreach (var missing in new[] { "Released as", "First released", "Label", "Genre", "How it sounds", "This song is" }) Assert.DoesNotContain(missing, bare);
        Assert.Null(DjWriter.Check("First out in 2005: Smack That, Akon with Eminem.", DjWriter.FactsOf(Req(), Template, now), []));   // a year it was not given
    }

    [Fact]
    public void MusicBrainz_facts_come_only_from_close_matches_and_the_feel_only_from_clear_cases()
    {
        var t = new Track("/m/smack.flac", "Smack That", "Akon, Eminem", "Konvicted", "Akon", 3, 1, 212_000, 2006, null, 0, TrackSource.Local);
        var close = new MbCandidate("r1", "Smack That", "Akon feat. Eminem", "Smack That", null, null, 2006, 212_000, "US", 100, "Single");
        var older = new MbCandidate("r2", "Smack That", "Akon", "Konvicted", null, null, 2005, 212_500, "GB", 100, "Album");
        var far = new MbCandidate("r3", "Smack That (Live)", "Someone Else", "Live Hits", null, null, 1999, 300_000, "DE", 40, "Live Compilation");
        var x = DjFactsOf.Brainz(t, [close, older, far])!;
        Assert.Equal("single", x.ReleaseType);
        Assert.Equal(2005, x.OriginalYear);   // the far match's 1999 is not believed
        Assert.Null(DjFactsOf.Brainz(t, [far]));
        Assert.Null(DjFactsOf.Brainz(t, null));
        Assert.Equal("a big one", DjFeel.Of(Big, null));
        Assert.Equal("slowing it down", DjFeel.Of(Calm, Big, picked: true));
        Assert.Equal("a calmer one", DjFeel.Of(Calm, Big));   // in their own playlist it describes the song (owner, 30 Oct)
        Assert.Equal("", DjFeel.Of(new Vibe(0.5, 0.5, 100, 0.5), new Vibe(0.55, 0.5, 104, 0.5)));
        Assert.Equal("", DjFeel.Of(null, Big));
    }

    [Fact]
    public async Task The_servers_facts_and_the_accounts_bond_reach_the_model()
    {
        var r = Make(_ => Task.FromResult(Model("Smack That, Akon with Eminem.")));
        r.Dj.Facts = (_, _) => (new DjExtra("album", 0, "", Big), DjBond.NewFind);
        await r.Dj.PrepareAsync(me, Req());
        var user = UserPrompt(Assert.Single(Calls(r, CloudflareVoice.TextModel)));
        Assert.Contains("Released as: an album", user);
        Assert.Contains("How it sounds: a big one", user);
        Assert.Contains("never played before", user);
    }

    // ---- the hello ----

    [Theory]
    [InlineData(DjStyle.Classic, 20, 5, "Good evening, Ashley. ")]
    [InlineData(DjStyle.Classic, 9, 1, "Monday morning, Ashley. ")]
    [InlineData(DjStyle.Chill, 20, 5, "Evening, Ashley. ")]
    [InlineData(DjStyle.Hype, 14, 3, "Hey Ashley, good afternoon. ")]
    [InlineData(DjStyle.Hype, 9, 6, "Hey Ashley, happy Saturday. ")]
    public void A_session_opens_with_a_hello_by_time_of_day_and_first_name(DjStyle style, int hour, int weekday, string start)
    {
        var l = DjPlanner.Plan(new DjSettings { Enabled = true, Style = style }, new DjRequest(DjSurface.Web, Smack, LocalHour: hour, Weekday: weekday),
            new DjMemory(), T0, new Random(1), null, "Ashley")!;
        Assert.StartsWith(start, l.Caption);
        Assert.Equal(DjSlot.Open, l.Slot);
        Assert.True(l.Parts![0].Personal);   // the clip with a name in it is kept for this person
        Assert.StartsWith("Good evening. ", DjPlanner.Plan(new DjSettings { Enabled = true }, new DjRequest(DjSurface.Web, Smack, LocalHour: 20), new DjMemory(), T0, new Random(1))!.Caption);
    }

    [Theory]
    [InlineData("ashley", "Ashley")]
    [InlineData("Mary Jane", "Mary")]
    [InlineData("jo.smith", "Jo")]
    [InlineData("admin", null)]
    [InlineData("guest", null)]
    [InlineData("ash@example.com", null)]
    [InlineData("user42", null)]
    [InlineData("", null)]
    public void The_first_name_comes_from_the_account_only_when_it_looks_like_a_name(string account, string? first) =>
        Assert.Equal(first, DjNames.FirstName(account));

    [Fact]
    public async Task The_server_greets_the_person_by_name_at_the_start_of_a_session()
    {
        var r = Make(voice: CloudflareVoice.MeloTts, ai: false);
        var cue = await r.Dj.PrepareAsync(me, new DjRequest(DjSurface.Web, Smack, LocalHour: 20, Weekday: 2));
        Assert.StartsWith("Good evening, Ashley.", cue!.Caption);
    }

    // ---- segues and time of day, sometimes ----

    [Fact]
    public void Segues_come_now_and_then_and_never_twice_in_three_lines()
    {
        var mem = new DjMemory(); var rng = new Random(11); var t = T0; var lines = new List<DjLine>();
        var prev = new DjSong("Song 0", "Artist 0", DurationMs: 200_000, Path: "/m/0.flac");
        for (var i = 1; i <= 600; i++)
        {
            var next = new DjSong("Song " + i, "Artist " + i % 17, DurationMs: 200_000, Path: $"/m/{i}.flac");
            if (DjPlanner.Plan(new DjSettings { Enabled = true }, new DjRequest(DjSurface.Web, next, prev), mem, t, rng) is { } l) lines.Add(l);
            prev = next; t += TimeSpan.FromMinutes(3);
        }
        var segues = lines.Count(l => l.Slot == DjSlot.Segue);
        Assert.InRange(segues, lines.Count / 20, lines.Count * 2 / 5);
        for (var i = 1; i < lines.Count; i++)
            if (lines[i].Slot == DjSlot.Segue) Assert.DoesNotContain(lines.Skip(Math.Max(0, i - 2)).Take(Math.Min(2, i)), x => x.Slot == DjSlot.Segue);
        Assert.Contains(lines, l => l.Slot == DjSlot.Segue && l.Caption.StartsWith("That was ") && l.Caption.Contains("..."));
    }

    [Fact]
    public void Late_night_and_Monday_morning_get_a_word_now_and_then()
    {
        Assert.Equal("Late night", DjPlanner.Colour(0, 3));
        Assert.Equal("Monday morning", DjPlanner.Colour(8, 1));
        Assert.Null(DjPlanner.Colour(15, 3));
        Assert.Null(DjPlanner.Colour(null, 1));
        var seen = false;
        for (var seed = 0; seed < 400 && !seen; seed++)
        {
            var mem = new DjMemory { LastRequest = T0 - TimeSpan.FromMinutes(3) };
            seen = DjPlanner.Plan(new DjSettings { Enabled = true }, new DjRequest(DjSurface.Web, Smack, Prev, LocalHour: 8, Weekday: 1), mem, T0, new Random(seed)) is { Slot: DjSlot.Time } l
                && l.Caption.Contains("Monday morning");
        }
        Assert.True(seen);
    }

    // ---- Style ----

    [Fact]
    public void Style_changes_the_prompt_the_templates_and_a_little_how_often()
    {
        var f = DjWriter.FactsOf(Req(), Template, now);
        Assert.Contains("Style: classic", DjWriter.Prompt(f, []).System);
        Assert.Contains("Style: chill", DjWriter.Prompt(f with { Style = DjStyle.Chill }, []).System);
        Assert.Contains("Style: hype", DjWriter.Prompt(DjWriter.FactsOf(Req(), Template, now, style: DjStyle.Hype), []).System);

        DjLine Open(DjStyle s, int seed) => DjPlanner.Plan(new DjSettings { Enabled = true, Style = s }, new DjRequest(DjSurface.Web, Smack), new DjMemory(), T0, new Random(seed))!;
        for (var seed = 0; seed < 20; seed++)
        {
            Assert.StartsWith("open-h", Open(DjStyle.Hype, seed).TemplateId);
            Assert.StartsWith("open-c", Open(DjStyle.Chill, seed).TemplateId);
            Assert.Matches("^open[0-9]$", Open(DjStyle.Classic, seed).TemplateId);
        }
        Assert.DoesNotContain(DjTemplates.All, t => t.Text.Contains('!'));

        int Spoken(DjStyle s)
        {
            var n = 0;
            for (var seed = 0; seed < 30; seed++)
            {
                var mem = new DjMemory(); var rng = new Random(seed); var t = T0; var prev = new DjSong("S0", "A0", DurationMs: 200_000, Path: "/0");
                for (var i = 1; i < 60; i++)
                {
                    var next = new DjSong("S" + i, "A" + i % 9, DurationMs: 200_000, Path: "/" + i);
                    if (DjPlanner.Plan(new DjSettings { Enabled = true, Style = s }, new DjRequest(DjSurface.Web, next, prev), mem, t, rng) is not null) n++;
                    prev = next; t += TimeSpan.FromMinutes(3);
                }
            }
            return n;
        }
        int chill = Spoken(DjStyle.Chill), classic = Spoken(DjStyle.Classic), hype = Spoken(DjStyle.Hype);
        Assert.True(chill < classic && classic < hype, $"{chill} {classic} {hype}");
        Assert.Equal(DjStyle.Classic, new DjSettings().Style);
    }

    [Fact]
    public async Task Style_is_kept_per_person_and_an_old_settings_file_is_Classic()
    {
        var r = Make(voice: CloudflareVoice.MeloTts, ai: false, style: DjStyle.Hype);
        Assert.Equal(DjStyle.Hype, r.Dj.Settings(me).Style);
        var old = Person("u9", "old");
        Assert.Equal(DjStyle.Classic, r.Dj.Settings(old).Style);
        var cue = await r.Dj.PrepareAsync(me, new DjRequest(DjSurface.Web, Smack));
        Assert.Contains(DjTemplates.All.Where(t => t.Style == DjStyle.Hype && t.Slot == DjSlot.Open), t => cue!.Caption.StartsWith(t.Text[..8]));
    }

    [Fact]
    public void Style_and_bond_go_over_the_wire()
    {
        var st = new DjSettings();
        Assert.Equal("classic", DjJson.Settings(st, false)["style"]!.GetValue<string>());
        Assert.Null(DjJson.Apply(System.Text.Json.Nodes.JsonNode.Parse("{\"style\":\"chill\"}")!.AsObject(), st));
        Assert.Equal(DjStyle.Chill, st.Style);
        Assert.NotNull(DjJson.Apply(System.Text.Json.Nodes.JsonNode.Parse("{\"style\":\"loud\"}")!.AsObject(), st));
        Assert.Equal(DjStyle.Chill, st.Style);   // nothing changed
        Assert.Equal("hype", DjJson.Patch(st, new DjSettings { Style = DjStyle.Hype })["style"]!.GetValue<string>());
        foreach (var bond in new[] { DjBond.Loved, DjBond.NewFind, DjBond.Unknown })
        {
            Assert.Null(DjJson.Request(DjJson.RequestBody(Req(bond: bond)), out var back));
            Assert.Equal(bond, back!.Bond);
        }
    }

    // ---- pooled lines and the cache ----

    [Fact]
    public async Task Early_in_the_day_lines_are_pooled_and_shared_parts_are_made_once_for_the_whole_family()
    {
        var bobby = Person("u2", "bobby");
        var r = Make(people: [me, bobby]);
        // 07:00: the day's reserve of pooled lines leaves no room for a whole line
        var a = await r.Dj.PrepareAsync(me, new DjRequest(DjSurface.Web, Smack, LocalHour: 7, Weekday: 3));
        Assert.NotNull(a?.ClipUrl);
        Assert.Empty(Calls(r, CloudflareVoice.TextModel));
        Assert.StartsWith("Good morning, Ashley. ", a.Caption);
        Assert.True(a.Caption.Contains("Smack That, by Akon with Eminem."), a.Caption);
        Assert.NotNull(await r.Clips.WaitAsync(IdOf(a.ClipUrl), TimeSpan.FromSeconds(10)));
        var b = await r.Dj.PrepareAsync(bobby, new DjRequest(DjSurface.Phone, Smack, LocalHour: 7, Weekday: 3));
        Assert.StartsWith("Good morning, Bobby. ", b!.Caption);
        Assert.NotNull(await r.Clips.WaitAsync(IdOf(b.ClipUrl!), TimeSpan.FromSeconds(10)));
        var said = Spoken(r);
        Assert.Equal(said.Count, said.Distinct().Count());   // nothing is ever made twice
        Assert.Equal(4, said.Count);                          // two hellos, the opener and the name: the last two shared
        Assert.Contains("Good morning, Ashley.", said); Assert.Contains("Good morning, Bobby.", said);
        Assert.Contains("Smack That, by Akon with Em-in-em.", said);
        Assert.Equal(2, r.Voice.SoFar.Said);
    }

    [Fact]
    public async Task In_the_evening_with_budget_left_the_line_is_whole_and_written_by_the_model()
    {
        var r = Make(_ => Task.FromResult(Model("Friday night, so here's Smack That, Akon with Eminem.")));
        var cue = await r.Dj.PrepareAsync(me, new DjRequest(DjSurface.Web, Smack, Prev, LocalHour: 21, Weekday: 5));
        Assert.Single(Calls(r, CloudflareVoice.TextModel));
        Assert.Equal("Friday night, so here's Smack That, Akon with Eminem.", cue!.Caption);
    }

    [Fact]
    public async Task The_same_words_in_the_same_voice_are_never_made_twice()
    {
        var r = Make();
        var id = r.Clips.Request("Coming up,")!;
        Assert.NotNull(await r.Clips.WaitAsync(id, TimeSpan.FromSeconds(10)));
        Assert.Equal(id, r.Clips.Request("Coming up,"));
        var made = await r.Clips.WarmAsync(DjPool.All(DjStyle.Classic));
        Assert.True(made > 5);
        Assert.Equal(0, await r.Clips.WarmAsync(DjPool.All(DjStyle.Classic)));   // once per voice
        var said = Spoken(r);
        Assert.Equal(said.Count, said.Distinct().Count());
        Assert.Single(said, s => s == "Coming up,");
        // a pooled line of phrases already made costs only its name clip
        var parts = new List<DjPart> { new("Coming up,", "Coming up,"), DjPool.Name("Yellow", "Yellow", "Coldplay", "Coldplay") };
        Assert.Equal(CloudflareVoice.EstimateNeurons(CloudflareVoice.DefaultVoice, "Yellow, by Coldplay."), r.Clips.CostOf(parts, "u1"), 6);
    }

    [Fact]
    public void Past_the_size_limit_the_least_recently_used_clips_go()
    {
        var r = Make();
        var clipDir = Directory.CreateDirectory(Path.Combine(dir, "a", "dj-clips")).FullName;
        for (var i = 0; i < 5; i++)
        {
            var f = Path.Combine(clipDir, i.ToString("x32") + ".mp3");
            File.WriteAllBytes(f, new byte[100]);
            File.SetLastWriteTimeUtc(f, new DateTime(2026, 1, 1).AddMinutes(i));
        }
        File.WriteAllText(Path.Combine(clipDir, "keep.txt"), "not a clip");
        r.Clips.Prune(300);
        Assert.Equal(3, Directory.GetFiles(clipDir, "*.mp3").Length);
        Assert.False(File.Exists(Path.Combine(clipDir, 0.ToString("x32") + ".mp3")));
        Assert.False(File.Exists(Path.Combine(clipDir, 1.ToString("x32") + ".mp3")));
        Assert.True(File.Exists(Path.Combine(clipDir, 4.ToString("x32") + ".mp3")));
        Assert.True(File.Exists(Path.Combine(clipDir, "keep.txt")));
        Assert.True(File.Exists(Path.Combine(clipDir, "index.json")));
        Assert.Equal(200L * 1024 * 1024, DjClips.MaxBytes);
    }

    [Fact]
    public async Task A_clip_used_lately_outlives_an_older_one_that_was_made_later()
    {
        var r = Make();
        var first = r.Clips.Request("An old line.")!;
        await r.Clips.WaitAsync(first, TimeSpan.FromSeconds(10));
        now = now.AddHours(1);
        var second = r.Clips.Request("A newer line.")!;
        await r.Clips.WaitAsync(second, TimeSpan.FromSeconds(10));
        now = now.AddHours(1);
        Assert.Equal(first, r.Clips.Request("An old line."));   // used again: the index says so
        var size = new FileInfo(Path.Combine(dir, "a", "dj-clips", first + ".mp3")).Length;
        r.Clips.Prune(size);
        Assert.True(File.Exists(Path.Combine(dir, "a", "dj-clips", first + ".mp3")));
        Assert.False(File.Exists(Path.Combine(dir, "a", "dj-clips", second + ".mp3")));
    }

    [Fact]
    public void Joined_clips_lose_their_tags()
    {
        byte[] tagged = [(byte)'I', (byte)'D', (byte)'3', 4, 0, 0, 0, 0, 0, 2, 9, 9, 0xFF, 0xFB, 1, 2];
        Assert.Equal(new byte[] { 0xFF, 0xFB, 1, 2 }, DjClips.Frames(tagged).ToArray());
        Assert.Equal(new byte[] { 0xFF, 0xFB }, DjClips.Frames([0xFF, 0xFB]).ToArray());
    }

    // ---- the budget ----

    [Fact]
    public void The_budget_lasts_all_day_well_over_100_Aura_lines_at_the_50_percent_cap()
    {
        const double cap = 5_000;
        var before = DjBudget.LinesPerDayWhole(cap);
        Assert.InRange(before, 30, 40);                       // the last round: every line whole, about 35 a day
        var (pooled0, whole0) = DjBudget.LinesPerDay(cap, 0);  // a first day: no name clip made before
        Assert.InRange(pooled0 + whole0, 110, 120);
        var (pooled3, whole3) = DjBudget.LinesPerDay(cap, 0.3);   // a family replaying favourites
        Assert.Equal(DjBudget.Floor, pooled3);
        Assert.InRange(whole3, 5, 12);
        Assert.True(pooled3 + whole3 > 125);
        // the model is a small part of a line
        Assert.InRange(DjModels.Primary.Cost(DjBudget.PromptTokens, DjBudget.AnswerTokens), 15, 30);
        Assert.All(DjModels.Weighed, m => Assert.True(m.Cost(DjBudget.PromptTokens, DjBudget.AnswerTokens) < 50, m.Id));
        // the reserve: none for whole lines at 07:00 on a fresh day, room in the evening; the dashboard's rough count
        var whole = DjBudget.WholeCost(DjModels.Primary);
        Assert.False(DjBudget.WholeAllowed(cap, 0, 0, 7, whole));
        Assert.True(DjBudget.WholeAllowed(cap, 0, 0, 20, whole));
        Assert.True(DjBudget.WholeAllowed(cap, 2_000, 60, 20, whole));
        Assert.False(DjBudget.WholeAllowed(cap, 4_950, 60, 20, whole));
        Assert.Equal(114, DjBudget.LinesLeft(cap, 0));
        Assert.Equal(0, DjBudget.LinesLeft(cap, 5_000));
    }
}
