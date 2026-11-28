using Microsoft.AspNetCore.Components;

namespace FLACie.Server;

/// <summary>The four page types of the one frame (docs/plans/one-app-consolidate.md).</summary>
public enum PageType { Standard, Full, Sheet, Plain }

/// <summary>
/// ONE APP 1k, the one frame: what the page on screen has declared (its type, title and up to two actions). The layout and the top bar draw the
/// back button, the title, the tab bar and the mini player from this, so no page draws its own header, back button or bars.
/// </summary>
public sealed class PageFrame
{
    public PageType Type { get; private set; } = PageType.Standard;
    public string? Title { get; private set; }
    public RenderFragment? Actions { get; private set; }
    /// <summary>The page has a hero with its own heading (an album, an artist, a playlist): the top bar's title is then plain text, so the page has one heading.</summary>
    public bool Hero { get; private set; }
    public event Action? Changed;

    public void Set(PageType type, string? title, RenderFragment? actions, bool hero = false)
    {
        // a page hands over a new actions fragment on every render: that alone is not a change (raising one re-rendered the layout, which
        // re-rendered the page, which handed over another: a loop). The new fragment is kept and drawn on the top bar's next render.
        var hadActions = Actions is not null;
        Actions = actions;
        if (type == Type && title == Title && hero == Hero && hadActions == (actions is not null)) return;
        Type = type; Title = title; Hero = hero;
        Changed?.Invoke();
    }

    /// <summary>A move to another page starts from a plain Standard page until the new page says what it is.</summary>
    public void Reset() { if (Type == PageType.Standard && Title is null && Actions is null) return; Type = PageType.Standard; Title = null; Actions = null; Hero = false; Changed?.Invoke(); }

    static string PathOf(string path) => "/" + path.Split('?', '#')[0].Trim('/');

    /// <summary>The five tab roots have no back button; every other Standard page has it, top left.</summary>
    public static bool IsTabRoot(string path) => PathOf(path) is "/" or "/search" or "/library" or "/devices" or "/settings";

    /// <summary>The tab a page sits under, so the tab bar keeps it lit there.</summary>
    public static string TabFor(string path)
    {
        var p = PathOf(path);
        bool Under(params string[] xs) => xs.Any(x => p == x || p.StartsWith(x + "/", StringComparison.OrdinalIgnoreCase));
        if (p == "/") return "Home";
        if (Under("/search", "/explore", "/charts")) return "Search";
        if (Under("/devices", "/jam")) return "Devices";
        if (Under("/settings", "/account", "/downloads", "/dashboard", "/admin", "/headphones")) return "Settings";
        return "Library";
    }

    /// <summary>The title as the kit draws it: the words in the upright serif, the last one in italics.</summary>
    public static (string Lead, string Last) SplitTitle(string title)
    {
        var i = title.TrimEnd().LastIndexOf(' ');
        return i < 0 ? ("", title.Trim()) : (title[..(i + 1)], title[(i + 1)..].Trim());
    }
}
