using System.Net;
using System.Text.Json;
using FLACie.Core;
using FLACie.Server;
using FLACie.Server.Dj;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FLACie.Tests;

/// <summary>Lines written by the Workers AI text model: the prompt carries the facts, doubtful answers fall back to the templates, the cap counts the model too,
/// and names are respelt in model lines as in template lines. Cloudflare is faked: nothing leaves the machine.</summary>
public class DjWriterTests : IDisposable
{
    const string Account = "0123456789abcdef0123456789abcdef";
    readonly string dir = Directory.CreateTempSubdirectory("flacie-dj-writer").FullName;
    public void Dispose() { try { Directory.Delete(dir, true); } catch (Exception) { } }
    DateTime now = new(2026, 10, 25, 19, 0, 0, DateTimeKind.Utc);
    readonly UserSession me = new("jellyfin", new JellyfinAccount("http://jf.test", "u1", "tester", "tok"), null) { Id = "u1" };

    static readonly DjSong Prev = new("Hey Ya!", "Outkast", "Speakerboxxx/The Love Below", "Outkast", 2003, "Hip-Hop", 235_000, "/m/heyya.flac");
    static readonly DjSong Smack = new("Smack That", "Akon, Eminem", "Konvicted", "Akon", 2006, "Hip-Hop", 212_000, "/m/smack.flac");
    static DjRequest Req(string? why = null) => new(DjSurface.Web, Smack, Prev, LocalHour: 20, Weekday: 5, Why: why);
    static readonly DjLine Template = new(DjSlot.Intro, "intro1", "Next, Smack That by Akon with Em-in-em.", "Next, Smack That by Akon with Eminem.");

    /// <summary>The model's answers, as Cloudflare sends them; the clips are MP3s.</summary>
    static HttpResponseMessage Model(string line, int pin = 400, int pout = 30) =>
        DjVoiceTests.Json(JsonSerializer.Serialize(new { result = new { response = line, usage = new { prompt_tokens = pin, completion_tokens = pout } }, success = true, errors = Array.Empty<object>() }));

    sealed record Rig(DjService Dj, CloudflareVoice Voice, DjVoiceTests.Handler Http);

    Rig Make(Func<HttpRequestMessage, Task<HttpResponseMessage>> model, int cap = 50, string voice = CloudflareVoice.DefaultVoice, bool ai = true)
    {
        var paths = new DataPaths(dir); var dp = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(dir, "keys")));
        var h = new DjVoiceTests.Handler(r => r.RequestUri!.ToString().Contains("/@cf/meta/") ? model(r) : Task.FromResult(DjVoiceTests.Audio()));
        var v = new CloudflareVoice(paths, new DjVoiceTests.Factory(h), dp, new ConfigurationBuilder().Build(), new DjVoiceTests.Lines<CloudflareVoice>(), () => now);
        v.Save(new DjVoiceConfig { AccountId = Account, CapPercent = cap, Voice = voice, AiLines = ai }, "cf_fake_token");
        var clips = new DjClips(paths, v, new DjVoiceTests.Lines<DjClips>(), () => now);
        var dj = new DjService(new UserStateStore(paths, dp), v, clips, paths, new DjVoiceTests.Lines<DjService>(), () => now) { AiLines = true, WriteTimeout = TimeSpan.FromMilliseconds(300) };
        var st = dj.Settings(me); st.Enabled = true; dj.Save(me, st);
        return new(dj, v, h);
    }

    static List<(string Url, string Auth, string Body)> ModelCalls(Rig r) { lock (r.Http.Seen) return r.Http.Seen.Where(x => x.Url.Contains("/@cf/meta/")).ToList(); }
    static string UserPrompt(string body) => JsonDocument.Parse(body).RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;

    [Fact]
    public void The_prompt_carries_the_facts_and_the_character()
    {
        var f = DjWriter.FactsOf(Req("New to you · Because you play Outkast") with { Bond = DjBond.NewFind }, Template, now);
        var (sys, user) = DjWriter.Prompt(f, ["Good evening. Here's Yellow, Coldplay."]);
        Assert.Contains("British English", sys); Assert.Contains("No emojis", sys); Assert.Contains("Never invent", sys);
        foreach (var fact in new[] { "Next song: Smack That", "Artist: Akon with Eminem", "Album: Konvicted", "Year: 2006", "Previous song: Hey Ya! by Outkast",
                     "Time of day: evening", "Day: Friday", "Why this song was picked: Something new for you; Because you play Outkast", "a new find", "settled in",
                     "between songs", "Good evening. Here's Yellow, Coldplay." })
            Assert.Contains(fact, user);
        // a session's first song, after a skip
        var open = DjWriter.FactsOf(new DjRequest(DjSurface.Web, Smack, null, PrevPlayedOut: false), Template with { Slot = DjSlot.Open }, now);
        var (_, u2) = DjWriter.Prompt(open, []);
        Assert.Contains("start of a listening session", u2); Assert.Contains("just skipped", u2); Assert.DoesNotContain("Previous song", u2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Here's a song I think you'll like.")]                                                             // names neither the song nor the artist
    [InlineData("Smack That, Akon with Eminem, number one in Germany and a Grammy nominee, which is a lot.")]       // invents (Germany, Grammy)
    [InlineData("Smack That from 2005, Akon with Eminem.")]                                                         // the wrong year
    [InlineData("Absolute banger incoming: Smack That, Akon with Eminem.")]                                        // cheesy
    [InlineData("Smack That, Akon with Eminem 🔥")]                                                                 // emoji
    [InlineData("Smack That #throwback")]
    [InlineData("Right then, here is a very long line about Smack That by Akon with Eminem that goes on and on well past what any presenter would ever say before a song starts.")]
    [InlineData("Smack That. Akon. Eminem. Konvicted. Lovely.")]                                                   // more than two sentences
    public void Doubtful_model_lines_are_refused(string raw)
    {
        var f = DjWriter.FactsOf(Req(), Template, now);
        Assert.Null(DjWriter.Check(raw, f, []));
    }

    [Fact]
    public void Quotes_markdown_and_a_speaker_label_are_stripped()
    {
        var f = DjWriter.FactsOf(Req(), Template, now);
        Assert.Equal("Friday evening, and here's Smack That, Akon with Eminem, from 2006.",
            DjWriter.Check("DJ: \"Friday evening, and here's **Smack That**, Akon with Eminem, from 2006.\"\nSecond line", f, []));
    }

    [Fact]
    public void A_line_that_repeats_or_opens_like_the_last_one_is_refused()
    {
        var f = DjWriter.FactsOf(Req(), Template, now);
        Assert.Null(DjWriter.Check("Here's Smack That, Akon with Eminem.", f, ["Here's Smack That, Akon with Eminem."]));
        Assert.Null(DjWriter.Check("Coming up, Smack That from Akon with Eminem.", f, ["Coming up, Yellow from Coldplay."]));
        Assert.NotNull(DjWriter.Check("Akon with Eminem now, Smack That.", f, ["Coming up, Yellow from Coldplay."]));
    }

    [Fact]
    public void Eminem_and_friends_are_respelt_in_model_lines_and_template_lines()
    {
        var say = DjPronounce.Default;
        var f = DjWriter.FactsOf(Req(), Template, now);
        var spoken = DjWriter.Speak("From 2006, Smack That, Akon with Eminem.", f, say);
        Assert.Equal("From two thousand and six, Smack That, Akon with Em-in-em.", spoken);
        // other names wherever the model mentions them
        foreach (var (w, s) in new[] { ("Sade", "Shar-day"), ("Björk", "Byerk"), ("Beyonce", "Bee-yon-say"), ("Ne-Yo", "Nee-yo"), ("will.i.am", "Will I Am"),
                     ("Rihanna", "Ree-anna"), ("Ke$ha", "Kesha"), ("A$AP Rocky", "A-sap Rocky"), ("Jay-Z", "Jay Zee"), ("The Prodigy", "The Prod-ij-ee"), ("Moby", "Moe-bee"),
                     ("Avicii", "Uh-vee-chee"), ("Tiësto", "Tee-esto"), ("Måneskin", "Mor-ness-kin"), ("Sigur Rós", "Siggur Rose") })
        {
            var g = DjWriter.FactsOf(new DjRequest(DjSurface.Web, new DjSong("Song", w), Prev), Template, now);
            Assert.Equal("Next, Song by " + s + ".", DjWriter.Speak("Next, Song by " + w + ".", g, say));
        }
        // and the planner's own template lines
        var line = DjPlanner.Plan(new DjSettings { Enabled = true }, new DjRequest(DjSurface.Web, new DjSong("Lose Yourself", "Eminem", DurationMs: 300_000, Path: "/m/l.flac")),
            new DjMemory(), now, new Random(1), say);
        Assert.NotNull(line);
        Assert.Contains("Em-in-em", line.Text); Assert.Contains("Eminem", line.Caption);
    }

    [Fact]
    public async Task A_good_model_line_is_said_and_shown_and_its_neurons_are_counted()
    {
        var r = Make(_ => Task.FromResult(Model("Friday evening, so here's Smack That, Akon with Eminem, from 2006.")));
        var cue = await r.Dj.PrepareAsync(me, Req());
        Assert.NotNull(cue);
        Assert.Equal("Friday evening, so here's Smack That, Akon with Eminem, from 2006.", cue.Caption);
        Assert.Equal("Friday evening, so here's Smack That, Akon with Em-in-em, from two thousand and six.", cue.Text);
        var call = Assert.Single(ModelCalls(r));
        Assert.Equal("Bearer cf_fake_token", call.Auth);
        Assert.EndsWith("/ai/run/" + CloudflareVoice.TextModel, call.Url);
        Assert.Contains("Next song: Smack That", UserPrompt(call.Body));
        // 400 tokens in, 30 out (Cloudflare's own count)
        var model = 400 * CloudflareVoice.TextInNeurons / 1e6 + 30 * CloudflareVoice.TextOutNeurons / 1e6;
        Assert.InRange(r.Voice.Usage.Neurons, model - 0.001, model + CloudflareVoice.EstimateNeurons(CloudflareVoice.DefaultVoice, cue.Text) + 0.001);   // (the clip may be under way)
        // asking again gives the same line without asking the model again
        Assert.Equal(cue.Text, (await r.Dj.PrepareAsync(me, Req()))!.Text);
        Assert.Single(ModelCalls(r));
    }

    [Fact]
    public async Task A_known_intro_goes_to_the_writer_as_a_word_target_and_to_the_player_in_the_cue()
    {
        var r = Make(_ => Task.FromResult(Model("Friday evening, here's Smack That, Akon with Eminem.")));
        r.Dj.Facts = (_, _) => (new DjExtra(Vibe: new Vibe(0.5, 0.5, 100, 0.5) { Intro = new SongIntro(6_000, 0.9, "lyrics") }), DjBond.Unknown);
        var cue = await r.Dj.PrepareAsync(me, Req());
        Assert.Equal(6_000, cue?.IntroMs);
        Assert.Contains("keep the line to about 13 words", UserPrompt(Assert.Single(ModelCalls(r)).Body));
        // not sure enough: neither
        var low = Make(_ => Task.FromResult(Model("Friday evening, here's Smack That, Akon with Eminem.")));
        low.Dj.Facts = (_, _) => (new DjExtra(Vibe: new Vibe(0.5, 0.5, 100, 0.5) { Intro = new SongIntro(6_000, 0.3, "levels") }), DjBond.Unknown);
        Assert.Null((await low.Dj.PrepareAsync(me, Req() with { Next = Smack with { Path = "/m/smack2.flac" } }))?.IntroMs);
    }

    [Fact]
    public async Task Invalid_or_long_model_output_falls_back_to_the_template()
    {
        var r = Make(_ => Task.FromResult(Model("Smack That by Akon with Eminem went to number one in nine countries, won a Grammy and sold millions, which honestly is quite something for a song.")));
        var cue = await r.Dj.PrepareAsync(me, Req());
        Assert.NotNull(cue);
        Assert.Single(ModelCalls(r));
        Assert.DoesNotContain("Grammy", cue.Caption);
        Assert.Contains("Smack That", cue.Caption);
        Assert.Contains("Em-in-em", cue.Text);   // the template line, respelt
        var bad = Make(_ => Task.FromResult(DjVoiceTests.Json("{\"success\":false,\"errors\":[{\"message\":\"no\"}]}", HttpStatusCode.BadRequest)));
        Assert.Contains("Smack That", (await bad.Dj.PrepareAsync(me, Req()))!.Caption);
    }

    [Fact]
    public async Task A_slow_model_falls_back_to_the_template_in_time()
    {
        var r = Make(async _ => { await Task.Delay(5000); return Model("Smack That, Akon with Eminem."); });
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var cue = await r.Dj.PrepareAsync(me, Req());
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), sw.Elapsed.ToString());
        Assert.NotNull(cue);
        Assert.Contains("Smack That", cue.Caption);
        Assert.Equal(TimeSpan.FromSeconds(2.5), DjWriter.Timeout);
    }

    [Fact]
    public async Task Near_the_cap_the_model_is_not_asked_and_the_template_is_said()
    {
        // 1% = 100 neurons: the model (about 15) fits, but not with room for an Aura line of 25 words (about 200)
        var r = Make(_ => Task.FromResult(Model("Smack That, Akon with Eminem.")), cap: 1);
        var cue = await r.Dj.PrepareAsync(me, Req());
        Assert.NotNull(cue);
        Assert.Empty(ModelCalls(r));
        Assert.Contains("Smack That", cue.Caption);
        Assert.Contains("cap", r.Voice.LastWrite);
        // a cap of 0: nothing is asked of Cloudflare at all (and the line is said only because its clip, in the same voice, is already made)
        Assert.NotNull(cue.ClipUrl);
        var made = Path.Combine(dir, "dj-clips", cue.ClipUrl["/api/dj/clip/".Length..]);
        for (var i = 0; i < 200 && !File.Exists(made); i++) await Task.Delay(25);
        var none = Make(_ => Task.FromResult(Model("Smack That, Akon with Eminem.")), cap: 0);
        var c0 = await none.Dj.PrepareAsync(me, Req());
        Assert.NotNull(c0); Assert.Empty(none.Http.Seen);   // (the clip from above is already on disk)
    }

    [Fact]
    public void The_model_estimate_is_small_and_an_aura_line_fits_the_cap_at_one_line_in_three_songs()
    {
        var (sys, user) = DjWriter.Prompt(DjWriter.FactsOf(Req("Because you play Outkast"), Template, now), ["One.", "Two.", "Three.", "Four.", "Five.", "Six."]);
        var text = CloudflareVoice.EstimateTextNeurons(sys, user);
        Assert.InRange(text, 5, 30);
        var aura = CloudflareVoice.EstimateNeurons(CloudflareVoice.DefaultVoice, "Friday evening, so here's Smack That, Akon with Em-in-em, from two thousand and six.");
        // at a 50% cap: about 30 lines a day, about 90 songs at one line in three
        Assert.InRange(5000 / (aura + text), 25, 60);
    }

    [Fact]
    public async Task Recent_lines_are_passed_so_the_model_avoids_them()
    {
        var n = 0;
        var r = Make(_ => Task.FromResult(Model(++n == 1 ? "Friday evening, so here's Smack That, Akon with Eminem." : "Outkast again now, with Ms. Jackson.")));
        await r.Dj.PrepareAsync(me, Req());
        // three songs later (Often: one silent song after a line, then a draw), the next line's prompt lists the first
        var songs = new[] { new DjSong("Ms. Jackson", "Outkast", "", "", 0, "", 200_000, "/m/1.flac"), new DjSong("Roses", "Outkast", "", "", 0, "", 200_000, "/m/2.flac"),
            new DjSong("Ms. Jackson", "Outkast", "", "", 0, "", 200_000, "/m/3.flac"), new DjSong("Roses", "Outkast", "", "", 0, "", 200_000, "/m/4.flac") };
        var prev = Smack;
        foreach (var s in songs) { now = now.AddMinutes(4); await r.Dj.PrepareAsync(me, new DjRequest(DjSurface.Web, s, prev)); prev = s; }
        var calls = ModelCalls(r);
        Assert.True(calls.Count >= 2, "the DJ spoke again");
        var later = UserPrompt(calls[^1].Body);
        Assert.Contains("Recent lines", later);
        Assert.Contains("Friday evening, so here's Smack That, Akon with Eminem.", later);
    }

    [Fact]
    public async Task Off_on_the_dashboard_means_templates_only()
    {
        var r = Make(_ => Task.FromResult(Model("Smack That, Akon with Eminem.")), ai: false);
        Assert.NotNull(await r.Dj.PrepareAsync(me, Req()));
        Assert.Empty(ModelCalls(r));
    }

    [Fact]
    public void Aura_Helios_is_the_default_and_an_old_melotts_file_moves_to_it_once()
    {
        Assert.Equal("aura-1:helios", new DjVoiceConfig().Voice);
        File.WriteAllText(Path.Combine(dir, "dj.json"), "{\"AccountId\":\"" + Account + "\",\"Token\":\"\",\"Voice\":\"melotts\",\"CapPercent\":50}");
        CloudflareVoice Load() => new(new DataPaths(dir), new DjVoiceTests.Factory(new DjVoiceTests.Handler(_ => Task.FromResult(DjVoiceTests.Audio()))),
            DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(dir, "keys"))), new ConfigurationBuilder().Build(), new DjVoiceTests.Lines<CloudflareVoice>(), () => now);
        var v = Load();
        Assert.Equal("aura-1:helios", v.Current.Voice);
        // chosen again after that, MeloTTS stays
        var c = v.Current; c.Voice = CloudflareVoice.MeloTts; v.Save(c, null);
        Assert.Equal(CloudflareVoice.MeloTts, Load().Current.Voice);
    }
}
