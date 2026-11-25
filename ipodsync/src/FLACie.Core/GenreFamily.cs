using System.Collections.Concurrent;

namespace FLACie.Core;

/// <summary>
/// Which scenes a genre tag belongs to, so tags that name one scene in different words count as near (owner, 25 Nov: after Heads Will Roll, tagged
/// "Indie Rock", the engine only knew other "Indie Rock" songs and the band's own; LCD Soundsystem is "Dance-Punk", MGMT "Alternative", Le Tigre
/// "Electroclash"). A tag can sit in two scenes (dance-punk is indie and electronic). Exact tags are nearest, a shared scene is near, nothing else is.
/// </summary>
public static class GenreFamily
{
    // the words of a tag, and the scenes each word puts it in. Checked word by word and as whole phrases, so "Indie Rock" is indie and rock.
    static readonly (string Word, string[] Families)[] Words =
    [
        ("indie", ["indie"]), ("indie rock", ["indie"]), ("indie pop", ["indie"]), ("alternative rock", ["indie", "rock"]), ("alternative", ["indie", "rock"]), ("alt", ["indie", "rock"]), ("post-punk", ["indie"]), ("post punk", ["indie"]), ("new wave", ["indie", "electronic"]),
        ("dance-punk", ["indie", "electronic"]), ("dance punk", ["indie", "electronic"]), ("garage rock", ["indie", "rock"]), ("britpop", ["indie", "rock"]),
        ("new rave", ["indie", "electronic"]), ("electroclash", ["indie", "electronic"]), ("shoegaze", ["indie"]), ("emo", ["indie", "rock"]), ("indietronica", ["indie", "electronic"]),
        ("electronic", ["electronic"]), ("electronica", ["electronic"]), ("electro", ["electronic"]), ("edm", ["electronic", "dance"]), ("house", ["electronic", "dance"]),
        ("techno", ["electronic", "dance"]), ("trance", ["electronic", "dance"]), ("dance", ["dance"]), ("eurodance", ["dance", "electronic"]), ("big beat", ["electronic", "dance"]),
        ("breakbeat", ["electronic", "dance"]), ("drum and bass", ["electronic", "dance"]), ("drum & bass", ["electronic", "dance"]), ("dnb", ["electronic", "dance"]),
        ("dubstep", ["electronic", "dance"]), ("rave", ["electronic", "dance"]), ("hardcore", ["electronic", "rock"]), ("synthpop", ["electronic", "indie"]),
        ("synth-pop", ["electronic", "indie"]), ("synth pop", ["electronic", "indie"]), ("disco", ["dance", "pop"]), ("nu-disco", ["dance", "electronic"]),
        ("idm", ["electronic"]), ("ambient", ["electronic", "chill"]), ("downtempo", ["electronic", "chill"]), ("trip hop", ["electronic", "chill"]), ("trip-hop", ["electronic", "chill"]),
        ("chillout", ["chill", "electronic"]), ("lo-fi", ["chill"]), ("lofi", ["chill"]), ("garage", ["electronic", "dance"]),
        ("pop", ["pop"]), ("dance pop", ["pop", "dance"]), ("dance-pop", ["pop", "dance"]), ("electropop", ["pop", "electronic"]), ("teen pop", ["pop"]), ("k-pop", ["pop"]),
        ("hip hop", ["hiphop"]), ("hip-hop", ["hiphop"]), ("rap", ["hiphop"]), ("trap", ["hiphop"]), ("grime", ["hiphop"]), ("drill", ["hiphop"]),
        ("r&b", ["rnb"]), ("rnb", ["rnb"]), ("soul", ["rnb"]), ("funk", ["rnb", "dance"]), ("neo soul", ["rnb"]),
        ("rock", ["rock"]), ("classic rock", ["rock"]), ("hard rock", ["rock"]), ("metal", ["rock"]), ("heavy metal", ["rock"]), ("punk", ["rock", "indie"]), ("grunge", ["rock"]),
        ("prog", ["rock"]), ("folk", ["folk"]), ("singer-songwriter", ["folk"]), ("acoustic", ["folk"]), ("country", ["folk"]), ("americana", ["folk"]),
        ("jazz", ["jazz"]), ("blues", ["blues"]), ("classical", ["classical"]), ("reggae", ["reggae"]), ("ska", ["reggae"]), ("soundtrack", ["soundtrack"]), ("score", ["soundtrack"]),
    ];

    static readonly ConcurrentDictionary<string, string[]> known = new(StringComparer.Ordinal);

    /// <summary>The scenes of a tag ("Dance-Punk" → indie, electronic); none for an empty or unknown one.</summary>
    public static string[] Of(string genre)
    {
        var g = (genre ?? "").Trim().ToLowerInvariant();
        if (g.Length == 0) return [];
        if (known.TryGetValue(g, out var f)) return f;
        var padded = " " + g.Replace('/', ' ').Replace(',', ' ').Replace(';', ' ') + " ";
        // the longest names first, each taking its words away: "Indie Rock" is the indie scene, not rock as well
        var found = new List<string>();
        foreach (var (word, families) in ByLength)
        {
            if (!padded.Contains(" " + word + " ", StringComparison.Ordinal)) continue;
            found.AddRange(families);
            padded = padded.Replace(" " + word + " ", "  ", StringComparison.Ordinal);
        }
        var res = found.Distinct().ToArray();
        if (known.Count < 5_000) known[g] = res;
        return res;
    }

    static readonly (string Word, string[] Families)[] ByLength = Words.OrderByDescending(w => w.Word.Length).ToArray();

    /// <summary>1 for the same tag, 0.6 when they share a scene, 0 otherwise (or when either is unknown).</summary>
    public static double Near(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;
        if (string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase)) return 1;
        var x = Of(a); var y = Of(b);
        return x.Length > 0 && y.Length > 0 && x.Any(y.Contains) ? 0.6 : 0;
    }
}
