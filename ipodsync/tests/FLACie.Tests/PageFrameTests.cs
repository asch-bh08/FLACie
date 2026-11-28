using FLACie.Server;
using Xunit;

namespace FLACie.Tests;

/// <summary>ONE APP 1k, the one frame (docs/plans/one-app-consolidate.md): every page is one of four types, the five tab roots have no back button,
/// every other Standard page has it in the same place, and a page under a tab keeps that tab lit.</summary>
public class PageFrameTests
{
    [Theory]
    [InlineData("/", true)] [InlineData("/search", true)] [InlineData("/library", true)] [InlineData("/devices", true)] [InlineData("/settings", true)]
    [InlineData("/album/x", false)] [InlineData("/playlists", false)] [InlineData("/jam", false)] [InlineData("/account", false)]
    public void Only_the_five_tab_roots_have_no_back_button(string path, bool root) => Assert.Equal(root, PageFrame.IsTabRoot(path));

    [Theory]
    [InlineData("/", "Home")] [InlineData("/search?q=a", "Search")] [InlineData("/songs", "Library")] [InlineData("/albums", "Library")] [InlineData("/album/k", "Library")]
    [InlineData("/artist/a", "Library")] [InlineData("/favorites", "Library")] [InlineData("/playlist/1/edit", "Library")] [InlineData("/explore", "Search")]
    [InlineData("/charts", "Search")] [InlineData("/devices", "Devices")] [InlineData("/jam", "Devices")] [InlineData("/settings", "Settings")]
    [InlineData("/account", "Settings")] [InlineData("/downloads", "Settings")] [InlineData("/dashboard", "Settings")] [InlineData("/headphones", "Settings")]
    public void A_page_keeps_its_tab_lit(string path, string tab) => Assert.Equal(tab, PageFrame.TabFor(path));

    [Fact]
    public void A_title_is_the_serif_with_its_last_word_in_italics()
    {
        Assert.Equal(("Your ", "library"), PageFrame.SplitTitle("Your library"));
        Assert.Equal(("", "Settings"), PageFrame.SplitTitle("Settings"));
    }

    [Fact]
    public void A_new_actions_fragment_on_every_render_is_not_a_change()
    {
        var f = new PageFrame(); var n = 0; f.Changed += () => n++;
        f.Set(PageType.Standard, "Edit playlist", b => b.AddContent(0, "Done"));
        f.Set(PageType.Standard, "Edit playlist", b => b.AddContent(0, "Done"));
        f.Set(PageType.Standard, "Edit playlist", b => b.AddContent(0, "Done"));
        Assert.Equal(1, n);
        Assert.NotNull(f.Actions);
    }

    [Fact]
    public void Setting_a_page_tells_the_frame_once_and_a_move_resets_it()
    {
        var f = new PageFrame(); var n = 0; f.Changed += () => n++;
        f.Set(PageType.Standard, "Jam", null);
        f.Set(PageType.Standard, "Jam", null);
        Assert.Equal(1, n);
        Assert.Equal("Jam", f.Title);
        f.Reset();
        Assert.Null(f.Title); Assert.Equal(PageType.Standard, f.Type);
    }
}
