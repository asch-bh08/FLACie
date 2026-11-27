using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace FLACie.Tests;

/// <summary>
/// The words people read are one list (docs/plans/one-app-words.md) on the web and the phone. This scans the web's .razor files (the text between tags,
/// attribute values and the C# string literals) and a few files that make user-facing strings, and fails on the spellings the list bans:
/// "Favorite", "..." (three dots), straight double quotes around a name, and "for offline". Code, ids, routes (/favorites) and comments are not looked at.
/// </summary>
public class WordsTests
{
    static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "ipodsync", "src", "FLACie.App"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    static string Src(params string[] parts) => Path.Combine([Root(), "ipodsync", "src", .. parts]);

    static IEnumerable<string> RazorFiles() =>
        new[] { "FLACie.App", "FLACie.Server" }
            .SelectMany(p => Directory.EnumerateFiles(Src(p), "*.razor", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    // C# files whose string literals are shown to people (toasts, download progress, errors)
    static readonly string[] CsFiles =
    [
        "FLACie.App/Toasts.cs", "FLACie.App/Feedback.cs", "FLACie.Core/Downloads.cs", "FLACie.Core/DownloadTags.cs", "FLACie.Server/DownloadManager.cs",
        "FLACie.Server/Upgrades.cs", "FLACie.Core/NasClient.cs", "FLACie.Server/ImportManager.cs",
    ];

    static readonly (string Name, Regex Re)[] Rules =
    [
        ("three dots (use the one … character)", new(@"\.\.\.")),
        ("straight double quotes (use curly “ ”)", new("\\\\\"|&quot;|\"")),
        ("Favorite (user text says Favourites)", new(@"(?<![\w./@#-])[Ff]avorites?\b|\b[Uu]nfavorite")),
        ("\"for offline\" (it is just Download)", new("for offline", RegexOptions.IgnoreCase)),
    ];

    /// <summary>The banned spellings in one piece of user text.</summary>
    public static IEnumerable<string> Banned(string text, bool codeLiteral)
    {
        foreach (var (name, re) in Rules)
        {
            // a literal's own delimiters are not text; markup written inside a C# string (class=\"x\") is not either
            var t = text;
            if (codeLiteral)
            {
                t = Regex.Replace(Regex.Replace(t, "^\\$?\"", ""), "\"$", "");
                if (Regex.IsMatch(t, "<[a-z][^>]*=\\\\\"", RegexOptions.IgnoreCase)) continue;
            }
            // what is left of C# in markup (== && => ;) is not prose
            if (name.StartsWith("straight") && (!Regex.IsMatch(t, "\\\\\"|&quot;|\"") || Regex.IsMatch(t, ";|==|&&|=>"))) continue;
            if (re.IsMatch(t)) yield return name;
        }
    }

    static string Blank(Match m) => Regex.Replace(m.Value, "[^\n]", " ");

    static string StripComments(string s)
    {
        s = Regex.Replace(s, @"@\*[\s\S]*?\*@", Blank);
        s = Regex.Replace(s, @"<!--[\s\S]*?-->", Blank);
        s = Regex.Replace(s, @"/\*[\s\S]*?\*/", Blank);
        s = Regex.Replace(s, @"(^|[ \t;{}(,])//[^\n]*", m => m.Groups[1].Value + Regex.Replace(m.Value[m.Groups[1].Length..], "[^\n]", " "), RegexOptions.Multiline);
        return s;
    }

    /// <summary>The razor with its C# parts (expressions, code blocks, conditions) blanked, so tags, text and attribute values are what is left.</summary>
    static string MarkupOnly(string s)
    {
        var o = new StringBuilder(); var i = 0; var n = s.Length;
        void SkipBalanced(char open, char close)
        {
            var d = 0;
            while (i < n)
            {
                var c = s[i];
                if (c == '"') { i++; while (i < n && s[i] != '"') { if (s[i] == '\\') i++; i++; } i++; continue; }
                if (c == '\'' && i + 2 < n && s[i + 2] == '\'') { i += 3; continue; }
                if (c == open) d++;
                else if (c == close) { d--; if (d == 0) { i++; return; } }
                i++;
            }
        }
        void Chain()
        {
            for (; ; )
            {
                if (i + 1 < n && s[i] == '.' && (char.IsLetter(s[i + 1]) || s[i + 1] == '_'))
                {
                    i++; while (i < n && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                    if (i < n && s[i] == '(') SkipBalanced('(', ')');
                }
                else if (i < n && s[i] == '[') SkipBalanced('[', ']');
                else break;
            }
        }
        while (i < n)
        {
            var c = s[i];
            if (c != '@') { o.Append(c); i++; continue; }
            var start = i;
            void Mark() => o.Append('@').Append(Regex.Replace(s[start..i], "[^\n]", ""));
            var rest = s[(i + 1)..Math.Min(n, i + 41)];
            if (i + 1 < n && s[i + 1] == '@') { o.Append("@@"); i += 2; continue; }
            if (i + 1 < n && s[i + 1] == '(') { i++; SkipBalanced('(', ')'); Chain(); Mark(); continue; }
            Match m;
            if ((m = Regex.Match(rest, @"^(code|functions)\s*\{")).Success) { i += m.Length; SkipBalanced('{', '}'); Mark(); continue; }
            if (i + 1 < n && s[i + 1] == '{') { i++; SkipBalanced('{', '}'); Mark(); continue; }
            if ((m = Regex.Match(rest, @"^(if|foreach|for|while|switch|lock|using)\s*\(")).Success) { i += m.Length; SkipBalanced('(', ')'); Mark(); continue; }
            if ((m = Regex.Match(rest, @"^[A-Za-z_][\w.]*")).Success)
            {
                i += 1 + m.Length;
                if (i < n && s[i] == '(') SkipBalanced('(', ')'); else if (i < n && s[i] == '[') SkipBalanced('[', ']');
                Chain(); Mark(); continue;
            }
            o.Append(c); i++;
        }
        return o.ToString();
    }

    static int LineOf(string s, int index) => s.AsSpan(0, index).Count('\n') + 1;

    /// <summary>What a person can read in a razor file: C# string literals, the text between tags, attribute values.</summary>
    public static IEnumerable<(string Text, int Line, bool Code)> RazorText(string raw)
    {
        var s = StripComments(raw);
        foreach (Match m in Regex.Matches(s, "\\$?\"(?:[^\"\\\\\\n]|\\\\.)*\"")) yield return (m.Value, LineOf(s, m.Index), true);
        var markup = MarkupOnly(s);
        foreach (Match m in Regex.Matches(markup, @">([^<>{}]*[^<>{}\s][^<>{}]*)<")) yield return (m.Groups[1].Value, LineOf(markup, m.Index), false);
        foreach (Match m in Regex.Matches(markup, "\\s[\\w:@.-]+=\"([^\"]*)\"")) yield return (m.Groups[1].Value, LineOf(markup, m.Index), false);
    }

    /// <summary>The user-facing string literals of a C# file (log lines are not user text).</summary>
    public static IEnumerable<(string Text, int Line, bool Code)> CsText(string raw)
    {
        var s = StripComments(raw);
        var lines = s.Split('\n');
        foreach (Match m in Regex.Matches(s, "\\$?\"(?:[^\"\\\\\\n]|\\\\.)*\""))
        {
            var line = LineOf(s, m.Index);
            if (Regex.IsMatch(lines[line - 1], @"\blog(ger)?\.|Log(Warn|Error|Info|Debug)|Console\.|Debug\.", RegexOptions.IgnoreCase)) continue;
            // a regex or a path pattern, not a sentence
            if (Regex.IsMatch(m.Value, @"\[[^\]]*\\\\")) continue;
            yield return (m.Value, line, true);
        }
    }

    static List<string> Offenders(string file, IEnumerable<(string Text, int Line, bool Code)> texts) =>
        texts.SelectMany(t => Banned(t.Text, t.Code).Select(r => $"{Path.GetFileName(file)}:{t.Line} {r}: {t.Text.Trim()[..Math.Min(80, t.Text.Trim().Length)]}")).Distinct().ToList();

    [Fact]
    public void The_web_pages_use_the_words_list()
    {
        var bad = RazorFiles().SelectMany(f => Offenders(f, RazorText(File.ReadAllText(f)))).ToList();
        Assert.True(bad.Count == 0, "Words not on the list (docs/plans/one-app-words.md):\n" + string.Join("\n", bad));
    }

    [Fact]
    public void The_web_messages_use_the_words_list()
    {
        var bad = CsFiles.SelectMany(f => Offenders(f, CsText(File.ReadAllText(Src(f.Split('/')))))).ToList();
        Assert.True(bad.Count == 0, "Words not on the list (docs/plans/one-app-words.md):\n" + string.Join("\n", bad));
    }

    [Theory]
    [InlineData("<button>Add to Favorites</button>", "Favorite")]
    [InlineData("<span>Unfavorite</span>", "Favorite")]
    [InlineData("<span>Searching...</span>", "three dots")]
    [InlineData("<p>Delete \"@p.Name\"?</p>", "straight")]
    [InlineData("<button aria-label=\"Download for offline\">x</button>", "for offline")]
    [InlineData("@code { void F() { Toast.Show(\"Saving...\"); } }", "three dots")]
    [InlineData("@code { void F() { Toast.Show($\"Removed \\\"{t}\\\"\"); } }", "straight")]
    public void The_scanner_catches_the_banned_spellings(string razor, string expected)
    {
        var found = RazorText(razor).SelectMany(t => Banned(t.Text, t.Code)).ToList();
        Assert.Contains(found, f => f.Contains(expected));
    }

    [Theory]
    [InlineData("<a href=\"/favorites\">Favourites</a>")]
    [InlineData("<span>Clear all favourites…</span>")]
    [InlineData("<p>Delete “@p.Name”?</p>")]
    [InlineData("@* the \"...\" menu, favorites *@ <b>Hi</b>")]
    [InlineData("@code { // Favorites...\n  void F() { var n = Session.Favorites.Count; Session.IsFavorite(t); } }")]
    public void The_scanner_leaves_code_routes_and_comments_alone(string razor)
    {
        var found = RazorText(razor).SelectMany(t => Banned(t.Text, t.Code)).ToList();
        Assert.Empty(found);
    }
}
