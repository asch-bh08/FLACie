using FLACie.Core;
using FLACie.Server;
using FLACie.Server.Dj;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;
using Xunit;

namespace FLACie.Tests;

/// <summary>The web player with the server's real DJ (only Cloudflare is faked): turned on with the Now Playing button, the listener hears it within a few songs.</summary>
public class PlayerDjLiveTests : IDisposable
{
    const string Account = "0123456789abcdef0123456789abcdef";
    readonly string dir = Directory.CreateTempSubdirectory("flacie-dj-live").FullName;
    public void Dispose() { try { Directory.Delete(dir, true); } catch (Exception) { } }

    sealed class Js : IJSRuntime
    {
        public readonly List<(string Id, object?[] Args)> Calls = [];
        /// <summary>The song playing, so each line is noted with the song it was said over.</summary>
        public Func<string?> Playing = () => null;
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args)
        {
            lock (Calls) Calls.Add((identifier, identifier is "flacie.djPlay" or "flacie.djSay" ? [.. args ?? [], Playing()] : args ?? []));
            return ValueTask.FromResult(default(T)!);
        }
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<T>(identifier, args);
        public int Spoken { get { lock (Calls) return Calls.Count(c => c.Id is "flacie.djPlay" or "flacie.djSay"); } }
        public int LastIndexOf(string id) { lock (Calls) return Calls.FindLastIndex(c => c.Id == id); }
        /// <summary>The songs that lines were said over, after call [i].</summary>
        public List<string> SpokenAfter(int i)
        {
            lock (Calls) return Calls.Skip(i + 1).Where(c => c.Id is "flacie.djPlay" or "flacie.djSay").Select(c => c.Args[^1] as string ?? "").ToList();
        }
    }

    sealed class NoPlanner : IAutoplayPlanner
    {
        public Task<List<CatalogSong>> RelatedAsync(Track seed, CancellationToken ct = default) => Task.FromResult(new List<CatalogSong>());
        public IReadOnlyList<CatalogSong> Fetching(UserSession s, IEnumerable<CatalogSong> suggested) => [];
        public void Prefetch(UserSession s, IEnumerable<CatalogSong> wanted, int max, Action onEach) { }
    }

    static readonly string[] Artists = ["Oasis", "Coldplay", "Blur", "Pulp", "Radiohead", "Suede", "Elbow", "Keane", "Muse", "Travis", "Doves", "Embrace", "Feeder", "Garbage", "Placebo", "Supergrass", "Ash", "Shed Seven", "Cast", "Ocean Colour Scene"];
    static readonly string[] Titles = ["Wonderwall", "Yellow", "Parklife", "Common People", "Karma Police", "Animal Nitrate", "One Day Like This", "Somewhere Only We Know", "Starlight", "Driftwood", "Pounding", "Gravity", "Buck Rogers", "Stupid Girl", "Pure Morning", "Alright", "Girl from Mars", "Going for Gold", "Walkaway", "The Day We Caught the Train"];
    static Track T(int n) => new($"/m/{n}.flac", Titles[n % 20], Artists[n % 20], "Album " + n, Artists[n % 20], 1, 1, 200_000, 1995 + n % 10, null, 0, TrackSource.Jellyfin, $"/m/{n}.flac", n.ToString("x32"), "Rock");

    (PlayerState P, Js Js, DjService Dj, DjVoiceTests.Handler Http, UserSession Me) Rig(bool enabled, bool? talkAfterSkips = null)
    {
        var paths = new DataPaths(dir); var dp = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(dir, "keys")));
        var h = new DjVoiceTests.Handler(_ => Task.FromResult(DjVoiceTests.Audio()));
        var v = new CloudflareVoice(paths, new DjVoiceTests.Factory(h), dp, new ConfigurationBuilder().Build(), new DjVoiceTests.Lines<CloudflareVoice>());
        v.Save(new DjVoiceConfig { AccountId = Account, CapPercent = 50 }, "cf_fake_token");
        var clips = new DjClips(paths, v, new DjVoiceTests.Lines<DjClips>());
        var dj = new DjService(new UserStateStore(paths, dp), v, clips, paths, new DjVoiceTests.Lines<DjService>());
        var me = new UserSession("jellyfin", new JellyfinAccount("http://jf.test", "u1", "tester", "tok"), null) { Id = "u1" };
        var st = dj.Settings(me); st.Enabled = enabled;
        if (talkAfterSkips is { } t) st.TalkAfterSkips = t;
        dj.Save(me, st);
        var js = new Js(); var p = new PlayerState(js); p.DjAskLead = null;   // (asked as a song starts, as before the 30 s lead)
        js.Playing = () => p.Current?.Title;
        // as on the web: a library, and the Autoplay lookup answering at once (the server keeps its answers for 30 minutes)
        p.SetLibrary(() => new Library(Enumerable.Range(0, 40).Select(T).ToList()));
        p.SetPlanner(me, new NoPlanner());
        p.SetDj(dj, DjSurface.Web);
        return (p, js, dj, h, me);
    }

    /// <summary>The asks run in the background: let them land before the song ends.</summary>
    static async Task Settle() { for (var i = 0; i < 5; i++) await Task.Yield(); await Task.Delay(20); }

    async Task<List<int>> PlayOut(PlayerState p, Js js, int songs)
    {
        var heardAt = new List<int>();
        for (var i = 0; i < songs; i++)
        {
            await Settle();
            var before = js.Spoken;
            p.OnTime(150, 200);
            await p.OnEnded();
            if (js.Spoken > before) heardAt.Add(i);
        }
        return heardAt;
    }

    [Fact]
    public async Task Turned_on_with_the_queue_button_after_some_skips_the_DJ_speaks_within_a_few_songs_played_to_the_end()
    {
        var (p, js, _, http, _) = Rig(enabled: false);
        await p.Play(Enumerable.Range(0, 20).Select(T).ToList(), 0);
        await p.ToggleDjAsync();
        Assert.True(p.DjOn);
        for (var i = 0; i < 3; i++) { await Settle(); await p.Next(); }   // skipped at once
        var heard = await PlayOut(p, js, 10);
        Assert.NotEmpty(heard);
        Assert.True(heard[0] <= 3, "first line only after song " + heard[0]);
        Assert.NotEmpty(http.Seen);
    }

    /// <summary>Skip one, let one play, over and over: "skipping going on" all the way through.</summary>
    static async Task<int> SkipAndPlay(PlayerState p, Js js, int rounds)
    {
        for (var i = 0; i < rounds; i++)
        {
            await Settle(); await p.Next();
            await Settle(); p.OnTime(150, 200); await p.OnEnded();
        }
        return js.Spoken;
    }

    [Fact]
    public async Task With_talk_after_skips_on_the_DJ_still_speaks_while_the_listener_keeps_skipping()
    {
        var (p, js, _, _, _) = Rig(enabled: true);
        await p.Play(Enumerable.Range(0, 30).Select(T).ToList(), 0);
        Assert.True(await SkipAndPlay(p, js, 12) >= 2);
    }

    [Fact]
    public async Task With_talk_after_skips_on_a_line_plays_over_a_song_reached_with_Next()
    {
        var (p, js, _, _, _) = Rig(enabled: true);
        await p.Play(Enumerable.Range(0, 20).Select(T).ToList(), 0);
        await Settle();
        await p.Next();
        await Settle(); await Task.Delay(200);
        Assert.Equal(1, js.Spoken);
    }

    [Fact]
    public async Task Three_quick_Nexts_never_stack_lines_only_the_last_songs_line_may_play()
    {
        var (p, js, _, _, _) = Rig(enabled: true);
        await p.Play(Enumerable.Range(0, 20).Select(T).ToList(), 0);
        await Settle();
        await p.Next(); await p.Next(); await p.Next();
        var stoppedAt = js.LastIndexOf("flacie.djStop");
        await Task.Delay(3500);
        // whatever still talks after the last skip was asked for the last song (Song 3), and at most one line
        var after = js.SpokenAfter(stoppedAt);
        Assert.True(after.Count <= 1, string.Join(" | ", after));
        Assert.All(after, t => Assert.Equal(Titles[3], t));
    }

    [Fact]
    public async Task With_talk_after_skips_off_Next_never_brings_a_line()
    {
        var (p, js, _, _, _) = Rig(enabled: true, talkAfterSkips: false);
        await p.Play(Enumerable.Range(0, 20).Select(T).ToList(), 0);
        await Settle();
        await p.Next();
        await Settle(); await Task.Delay(200);
        Assert.Equal(0, js.Spoken);
    }

    [Fact]
    public async Task With_talk_after_skips_off_skipping_keeps_the_DJ_quiet_as_before()
    {
        var (p, js, _, _, _) = Rig(enabled: true, talkAfterSkips: false);
        await p.Play(Enumerable.Range(0, 30).Select(T).ToList(), 0);
        await SkipAndPlay(p, js, 12);
        Assert.Equal(0, js.Spoken);
    }
}
