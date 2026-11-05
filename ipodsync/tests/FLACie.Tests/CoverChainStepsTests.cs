using System.Net;
using System.Text.Json.Nodes;
using FLACie.Core;
using FLACie.Server;
using Xunit;

namespace FLACie.Tests;

/// <summary>F1: the full cover chain before the made cover: catalog, folder picture, artist photo on Jellyfin, catalog artist photo. One test per step.</summary>
public class CoverChainStepsTests
{
    static readonly JsonNode Song = JsonNode.Parse("""
        {"Id": "s1", "Name": "Lose Yourself", "Path": "/music/Eminem/Singles/Eminem_Lose_Yourself.mp3.mp3", "Artists": ["Eminem"], "Album": "",
         "ParentId": "fold1", "ArtistItems": [{"Name": "Eminem", "Id": "art9"}]}
        """)!;

    static string Key => JellyfinClient.ToTrack("http://jf", Song).ArtKey!;

    [Fact]
    public void A_song_without_a_picture_plans_catalog_folder_artist_then_catalog_artist()
    {
        var plan = CoverChain.Plan(Key);
        Assert.Equal([CoverStep.Kinds.Catalog, CoverStep.Kinds.JellyfinPicture, CoverStep.Kinds.JellyfinArtist, CoverStep.Kinds.CatalogArtist], plan.Select(p => p.Kind));
        Assert.Equal("Eminem", plan[0].Value); Assert.Equal("Lose Yourself", plan[0].Title);
        Assert.Equal("jffold1", plan[1].Value);
        Assert.Equal("art9", plan[2].Value);
        Assert.Equal("Eminem", plan[3].Value);
        Assert.Equal(("Eminem", "", "Lose Yourself"), Art.ParseLookupKey(Key));   // the catalog is still asked by the song
    }

    [Fact]
    public void A_folder_picture_with_a_tag_is_kept_with_its_tag()
    {
        var o = Song.DeepClone(); o["ParentPrimaryImageItemId"] = "alb2"; o["ParentPrimaryImageTag"] = "t7";
        Assert.Equal("jfalb2~t7", CoverChain.Plan(JellyfinClient.ToTrack("http://jf", o).ArtKey!)[1].Value);
    }

    static Func<CoverStep, CancellationToken, Task<(byte[]?, bool)>> OnlyAt(CoverStep.Kinds kind, List<CoverStep.Kinds> asked) => (st, _) =>
    {
        asked.Add(st.Kind);
        return Task.FromResult<(byte[]?, bool)>(st.Kind == kind ? ([1, 2, 3], false) : (null, false));
    };

    [Theory]
    [InlineData(CoverStep.Kinds.Catalog, 1)]
    [InlineData(CoverStep.Kinds.JellyfinPicture, 2)]
    [InlineData(CoverStep.Kinds.JellyfinArtist, 3)]
    [InlineData(CoverStep.Kinds.CatalogArtist, 4)]
    public async Task Each_step_is_tried_in_turn_and_the_first_picture_wins(CoverStep.Kinds at, int tries)
    {
        var asked = new List<CoverStep.Kinds>();
        var (bytes, from, transient) = await CoverChain.RunAsync(CoverChain.Plan(Key), OnlyAt(at, asked), default);
        Assert.NotNull(bytes);
        Assert.Equal(at, from!.Kind);
        Assert.Equal(tries, asked.Count);
        Assert.False(transient);
    }

    [Fact]
    public async Task Nothing_anywhere_ends_in_the_made_cover_and_a_step_not_asked_means_try_again()
    {
        var (bytes, _, transient) = await CoverChain.RunAsync(CoverChain.Plan(Key), (_, _) => Task.FromResult<(byte[]?, bool)>((null, false)), default);
        Assert.Null(bytes);
        Assert.Equal(CoverMiss.Generated, GeneratedCover.Answer(transient, false));
        (_, _, transient) = await CoverChain.RunAsync(CoverChain.Plan(Key), (st, _) => Task.FromResult<(byte[]?, bool)>((null, st.Kind == CoverStep.Kinds.CatalogArtist)), default);
        Assert.Equal(CoverMiss.TryAgain, GeneratedCover.Answer(transient, false));
    }

    sealed class Deezer(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }

    [Fact]
    public async Task The_catalog_artist_photo_is_the_same_artist_and_never_the_blank_stand_in()
    {
        var found = await new WebCatalog(new HttpClient(new Deezer("""{"data": [{"name": "Eminem Tribute", "picture_xl": "https://e/x/1.jpg"}, {"name": "Eminem", "picture_xl": "https://e-cdns-images.dzcdn.net/images/artist/abc/1000x1000.jpg"}]}"""))).ArtistPictureAsync("Eminem");
        Assert.Equal("https://e-cdns-images.dzcdn.net/images/artist/abc/1000x1000.jpg", found.Url);
        var blank = await new WebCatalog(new HttpClient(new Deezer("""{"data": [{"name": "Eminem", "picture_xl": "https://e-cdns-images.dzcdn.net/images/artist//1000x1000.jpg"}]}"""))).ArtistPictureAsync("Eminem");
        Assert.Null(blank.Url);
    }
}
