using System.Net;
using FLACie.Core;
using FLACie.Server;
using Xunit;

namespace FLACie.Tests;

/// <summary>
/// Owner, 5 Nov: many web covers stayed blank ("How You Remind Me" on a Now 1's, "Lose Yourself" on a Rolling Stone list) and others took ages. The catalog had
/// them; the lookup had been cut short (timed out, Deezer's quota, the browser gave up) and that was remembered as "no cover". Only a real "the catalog has
/// nothing" is remembered, and then only for an hour.
/// </summary>
public class CatalogCoverTests
{
    sealed class Script(Func<HttpRequestMessage, int, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => Task.FromResult(answer(r, Interlocked.Increment(ref Calls)));
    }
    static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    static HttpResponseMessage Picture() => new(HttpStatusCode.OK) { Content = new ByteArrayContent([0xFF, 0xD8, 0xFF, 1, 2, 3]) };
    static readonly RetryPolicy NoWait = new() { Delay = (_, _) => Task.CompletedTask };
    static string Who() => "Nickelback " + Guid.NewGuid().ToString("N")[..6];
    static string Track(string who) => """{"data":[{"title":"How You Remind Me","artist":{"name":"WHO"},"album":{"title":"Silver Side Up","cover_big":"https://e.example/ssu.jpg"}}]}""".Replace("WHO", who);
    const string Quota = """{"error":{"type":"Exception","message":"Quota limit exceeded","code":4}}""";

    [Fact]
    public async Task A_song_found_by_title_alone_gets_its_cover()
    {
        var who = Who();
        var h = new Script((r, _) => r.RequestUri!.Host == "e.example" ? Picture() : Json(Track(who)));
        var got = await MediaEndpoints.CatalogCover(new HttpClient(h), who, "", "How You Remind Me", default, NoWait);
        Assert.Equal(CatalogAnswer.Found, got.Answer);
        Assert.NotNull(got.Bytes);
    }

    [Fact]
    public async Task Deezers_quota_answer_is_not_no_cover()
    {
        var h = new Script((_, _) => Json(Quota));
        var got = await MediaEndpoints.CatalogCover(new HttpClient(h), Who(), "", "How You Remind Me", default, NoWait);
        Assert.Equal(CatalogAnswer.Unasked, got.Answer);
        Assert.False(CoverAnswers.RememberAsMiss(got.Answer));
    }

    [Fact]
    public async Task A_lookup_cut_short_is_not_no_cover_and_the_next_view_finds_it()
    {
        var who = Who();
        var dead = new Script((_, _) => throw new OperationCanceledException());
        Assert.Equal(CatalogAnswer.Unasked, (await MediaEndpoints.CatalogCover(new HttpClient(dead), who, "", "How You Remind Me", default, NoWait)).Answer);
        var back = new Script((r, _) => r.RequestUri!.Host == "e.example" ? Picture() : Json(Track(who)));
        Assert.Equal(CatalogAnswer.Found, (await MediaEndpoints.CatalogCover(new HttpClient(back), who, "", "How You Remind Me", default, NoWait)).Answer);
    }

    [Fact]
    public async Task A_found_cover_whose_picture_could_not_be_fetched_is_tried_again()
    {
        var who = Who();
        var h = new Script((r, _) => r.RequestUri!.Host == "e.example" ? new HttpResponseMessage(HttpStatusCode.BadGateway) : Json(Track(who)));
        Assert.Equal(CatalogAnswer.Unasked, (await MediaEndpoints.CatalogCover(new HttpClient(h), who, "", "How You Remind Me", default, NoWait)).Answer);
    }

    [Fact]
    public async Task Only_a_real_nothing_is_remembered()
    {
        var h = new Script((_, _) => Json("""{"data":[]}"""));
        var got = await MediaEndpoints.CatalogCover(new HttpClient(h), "floorage " + Guid.NewGuid().ToString("N")[..6], "", "hoodtrap remix", default, NoWait);
        Assert.Equal(CatalogAnswer.Nothing, got.Answer);
        Assert.True(CoverAnswers.RememberAsMiss(CatalogAnswer.Nothing));
        Assert.False(CoverAnswers.RememberAsMiss(CatalogAnswer.Unasked));
        Assert.Equal(TimeSpan.FromHours(1), CoverAnswers.MissKept);
    }

    [Fact]
    public async Task A_found_cover_url_is_kept_so_the_next_view_does_not_ask_the_catalog()
    {
        var who = Who();
        var h = new Script((r, _) => r.RequestUri!.Host == "e.example" ? Picture() : Json(Track(who)));
        await MediaEndpoints.CatalogCover(new HttpClient(h), who, "", "How You Remind Me", default, NoWait);
        var asked = h.Calls;
        await MediaEndpoints.CatalogCover(new HttpClient(h), who, "", "How You Remind Me", default, NoWait);
        Assert.Equal(asked + 1, h.Calls);   // only the picture itself, not the search
    }

    [Fact]
    public void A_transient_answer_lets_the_browser_ask_again_soon_and_a_real_miss_is_kept_briefly()
    {
        Assert.Equal((503, "no-store"), CoverAnswers.Missing(transient: true));
        var (status, cache) = CoverAnswers.Missing(transient: false);
        Assert.Equal(404, status);
        Assert.Contains("max-age=600", cache);
    }

    [Fact]
    public void A_timed_cache_forgets_after_its_time_and_drops_the_oldest_when_full()
    {
        var now = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
        var c = new TimedLru<string, int>(2, () => now);
        c.Set("a", 1, TimeSpan.FromHours(1));
        c.Set("b", 2, TimeSpan.FromHours(1));
        Assert.True(c.TryGet("a", out _));   // "a" is now the newest
        c.Set("c", 3, TimeSpan.FromHours(1));
        Assert.False(c.TryGet("b", out _));
        Assert.True(c.TryGet("a", out var a) && a == 1);
        now = now.AddHours(1).AddSeconds(1);
        Assert.False(c.TryGet("a", out _));
        Assert.False(c.TryGet("c", out _));
    }

    // songs that are in no catalog (hoodtrap remixes in a playlist folder): the folder's Jellyfin picture, carried in the key, only when the catalog has nothing
    [Fact]
    public void A_lookup_key_can_carry_a_fallback_picture_without_changing_the_saved_covers_key()
    {
        var plain = Art.LookupKey("floorage", "", "Lovers Rock (hoodtrap)")!;
        var withFallback = Art.LookupKey("floorage", "", "Lovers Rock (hoodtrap)", "jfabc~tag")!;
        Assert.NotEqual(plain, withFallback);
        Assert.Equal(plain, Art.LookupBase(withFallback));
        Assert.Equal(plain, Art.LookupBase(plain));
        Assert.Equal("jfabc~tag", Art.LookupFallback(withFallback));
        Assert.Null(Art.LookupFallback(plain));
        Assert.Equal(("floorage", "", "Lovers Rock (hoodtrap)"), Art.ParseLookupKey(withFallback));
        // an untagged or non-Jellyfin fallback is not carried
        Assert.Equal(plain, Art.LookupKey("floorage", "", "Lovers Rock (hoodtrap)", "jfabc"));
    }

    [Fact]
    public void A_mixed_folder_song_keeps_its_folder_picture_as_the_fallback()
    {
        static string Id(int n) => n.ToString("x32");
        const string dir = "/media/Movies/music/HoodTrap(LAC)/";
        Track Jf(int n, string album, string artist) =>
            new($"http://jf/Audio/{Id(n)}/stream?static=true", $"Song {n}", artist, album, artist, n, 1, 1000, 2000, "jf" + Id(900) + "~folder", 0, TrackSource.Jellyfin, dir + n + ".flac", Id(n));
        var vetted = CoverVetting.Vet([Jf(1, "Lovers Rock", "floorage"), Jf(2, "Other", "someone")]);
        Assert.Equal("jf" + Id(900) + "~folder", Art.LookupFallback(vetted[0].ArtKey!));
        Assert.Equal(Art.LookupKey("floorage", "", "Song 1"), Art.LookupBase(vetted[0].ArtKey!));
    }
}
