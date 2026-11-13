namespace FLACie.Core;

/// <summary>
/// The real names of a song's other versions (owner, 13 Nov: "make sure they're titled differently ... give it the name of the actual version"): the
/// server looks each one up in the catalog by its album and remembers it by file path ("05 Edit" for the Voodoo People on Their Law). Library reads them
/// through <see cref="Lookup"/>.
/// </summary>
public static class VersionNames
{
    /// <summary>The name found for a file, "" when the catalog's title has no version part, null when not looked up yet.</summary>
    public static Func<string, string?>? Lookup { get; set; }

    /// <summary>The version part of the catalog's title of a song whose file is titled [fileTitle]: what is in its last brackets or after " - "
    /// ("Voodoo People (05 Edit)" → "05 Edit"); "" when the catalog's title is the plain song or another song.</summary>
    public static string NameFrom(string catalogTitle, string fileTitle)
    {
        var c = catalogTitle.Trim();
        var core = Matching.NormTitle(fileTitle);
        string? part = null, head = null;
        var dash = c.LastIndexOf(" - ", StringComparison.Ordinal);
        if (c.EndsWith(')') && c.LastIndexOf('(') is var open and > 0) { part = c[(open + 1)..^1]; head = c[..open]; }
        else if (c.EndsWith(']') && c.LastIndexOf('[') is var sq and > 0) { part = c[(sq + 1)..^1]; head = c[..sq]; }
        else if (dash > 0) { part = c[(dash + 3)..]; head = c[..dash]; }
        if (part is null || head is null || Matching.NormTitle(head) != core) return "";
        part = part.Trim();
        return part.Length is > 0 and <= 60 ? part : "";
    }
}
