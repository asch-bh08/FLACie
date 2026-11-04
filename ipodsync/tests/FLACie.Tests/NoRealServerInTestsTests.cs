using System.Text.RegularExpressions;
using Xunit;

namespace FLACie.Tests;

/// <summary>
/// Tests and test tools must never be able to reach the owner's real Jellyfin (old test runs left TEST-* playlists on it, 2 Nov 2026).
/// Three guards, all checked here: (1) no test or tool file names an address of the owner's (a Tailscale 100.64/10 address, a *.ts.net name other
/// than the example.ts.net placeholders, the file-mover host as a URL host, :8096 on a LAN address, or an env fallback to a Jellyfin URL);
/// (2) both mock Jellyfins answer the marker route GET /__mock, and every harness or driver that talks to a Jellyfin checks it before it does;
/// (3) every script that clears or uninstalls the app on a device first insists the device is an emulator.
/// The patterns are shapes, not the owner's literal addresses, so this file does not put them into the repository.
/// </summary>
public class NoRealServerInTestsTests
{
    static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "THIRD-PARTY-NOTICES.md"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    static readonly string[] Dirs = { "ipodsync/tests", "ipodsync/tools", "android/app/src/test", "android/app/src/androidTest", "android/tools", ".github" };
    static readonly string[] Exts = { ".cs", ".kt", ".kts", ".mjs", ".js", ".ts", ".ps1", ".sh", ".py", ".json", ".yml", ".yaml", ".md", ".xml", ".csproj" };
    static readonly string[] SkipDirs = { "node_modules", "bin", "obj", ".data-smoke", ".pub-smoke", "test-results", "playwright-report", ".pub-real", ".data-real" };
    const string Self = "NoRealServerInTestsTests.cs";

    static IEnumerable<string> Files()
    {
        var root = Root();
        foreach (var rel in Dirs)
        {
            var dir = Path.Combine(root, rel);
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                var parts = f.Substring(root.Length).Split('\\', '/');
                if (parts.Any(p => SkipDirs.Contains(p))) continue;
                if (Path.GetFileName(f) == Self || Path.GetFileName(f).StartsWith("result-")) continue;   // result-*.json are measurements, not configuration
                if (Exts.Contains(Path.GetExtension(f).ToLowerInvariant())) yield return f;
            }
        }
    }

    /// <summary>What counts as one of the owner's addresses, as shapes. Returns a reason, or null when the line is fine.</summary>
    internal static string? Offence(string line)
    {
        if (Regex.IsMatch(line, @"\b100\.(6[4-9]|[7-9]\d|1[01]\d|12[0-7])\.\d{1,3}\.\d{1,3}\b")) return "a Tailscale address (100.64.0.0/10)";
        foreach (Match m in Regex.Matches(line, @"\b((?:[a-z0-9-]+\.)+)ts\.net\b", RegexOptions.IgnoreCase))
            if (!Regex.IsMatch(m.Value, @"(^|\.)example\.ts\.net$", RegexOptions.IgnoreCase)) return "a Tailscale name (" + m.Value + ")";
        if (Regex.IsMatch(line, @"(://|@)frank\b", RegexOptions.IgnoreCase)) return "the owner's server as a host name";
        if (Regex.IsMatch(line, @"\b(192\.168|172\.(1[6-9]|2\d|3[01])|10)\.\d{1,3}\.\d{1,3}(\.\d{1,3})?:8096\b") && !Regex.IsMatch(line, @"10\.0\.2\.2:8096")) return "a LAN address on Jellyfin's port 8096";
        if (Regex.IsMatch(line, @"FLACIE_JELLYFIN(_INTERNAL)?_URL(:-|\s*\?\?|\s*\|\|)")) return "a fallback for the Jellyfin address (a tool that needs a target must be given one)";
        return null;
    }

    [Fact]
    public void No_test_or_tool_file_names_the_owners_servers()
    {
        var bad = new List<string>();
        foreach (var f in Files())
        {
            var n = 0;
            foreach (var line in File.ReadLines(f))
            {
                n++;
                if (Offence(line) is { } why) bad.Add($"{Path.GetRelativePath(Root(), f)}:{n}  {why}");
            }
        }
        Assert.True(bad.Count == 0, "Tests and test tools must not mention the owner's servers; use a mock (127.0.0.1, 10.0.2.2) or an example address (192.0.2.x, example.ts.net):\n" + string.Join("\n", bad));
    }

    [Theory]
    [InlineData("http://" + "100.100.1.2" + ":8096/Audio/x", true)]
    [InlineData("https://box.tail1234.ts" + ".net/x", true)]
    [InlineData("https://jellyfin.example.ts" + ".net/x", false)]
    [InlineData("ssh root@" + "frank", true)]
    [InlineData("http://" + "192.168.1.20" + ":8096", true)]
    [InlineData("http://192.0.2.20:8096", false)]
    [InlineData("http://10.0.2.2:8096", false)]
    [InlineData("http://127.0.0.1:8096", false)]
    [InlineData("FLACIE_JELLYFIN_URL=${FLACIE_JELLYFIN_URL:-http://x}", true)]
    [InlineData("a comment about frank, 21:33: the pooled phrases", false)]
    public void The_scan_recognises_the_shapes(string line, bool offence) => Assert.Equal(offence, Offence(line) is not null);

    [Fact]
    public void Both_mocks_answer_the_marker_route()
    {
        foreach (var rel in new[] { "ipodsync/tests/web-smoke/mock-jellyfin.mjs", "ipodsync/tools/loadtest/mock.mjs" })
        {
            var text = File.ReadAllText(Path.Combine(Root(), rel));
            Assert.Matches(@"p === '/__mock'\)\s*return json\(res, 200, \{ mock: true \}\)", text);
        }
    }

    static readonly string[] MarkerTokens = { "__mock", "requireMock", "require_test_jellyfin" };

    [Fact]
    public void Every_harness_that_talks_to_a_Jellyfin_checks_the_marker_first()
    {
        // scripts that sign in to a Jellyfin or start a server with a Jellyfin address; specs and the Playwright config are covered by global-setup.ts
        var talks = new Regex(@"FLACIE_JELLYFIN_URL|Users/AuthenticateByName|QuickConnect/Initiate|System/Info/Public");
        var bad = new List<string>();
        foreach (var f in Files())
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            var name = Path.GetFileName(f);
            if (ext is not (".mjs" or ".ts" or ".sh" or ".ps1" or ".js")) continue;
            if (name.EndsWith(".spec.ts") || name is "playwright.config.ts" or "sharp-helpers.ts" or "start-test-jellyfins.sh") continue;
            var text = File.ReadAllText(f);
            if (talks.IsMatch(text) && !MarkerTokens.Any(text.Contains) && !text.Contains("FLACie test")) bad.Add(Path.GetRelativePath(Root(), f));
        }
        Assert.True(bad.Count == 0, "These files talk to a Jellyfin without checking the mock marker (GET /__mock, see tools/loadtest/require-mock.mjs):\n" + string.Join("\n", bad));

        var setup = File.ReadAllText(Path.Combine(Root(), "ipodsync/tests/web-smoke/global-setup.ts"));
        Assert.Contains("/__mock", setup);
        var config = File.ReadAllText(Path.Combine(Root(), "ipodsync/tests/web-smoke/playwright.config.ts"));
        Assert.Contains("globalSetup", config);
    }

    [Fact]
    public void Every_script_that_clears_or_uninstalls_the_app_insists_on_an_emulator()
    {
        var wipes = new Regex(@"pm clear|(?i:adb) uninstall|'uninstall'");
        var bad = new List<string>();
        foreach (var f in Files())
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            if (ext is not (".mjs" or ".sh" or ".ps1" or ".js")) continue;
            var text = File.ReadAllText(f);
            if (!wipes.IsMatch(text)) continue;
            var name = Path.GetFileName(f);
            // adbui.mjs holds the guard itself (adb() refuses these commands off an emulator)
            if (!(text.Contains("requireEmulator") || text.Contains("emulator-") || text.Contains("^emulator"))) bad.Add(Path.GetRelativePath(Root(), f));
        }
        Assert.True(bad.Count == 0, "These scripts wipe the app without insisting on an emulator (emulator-NNNN):\n" + string.Join("\n", bad));
        Assert.Contains("emulator-", File.ReadAllText(Path.Combine(Root(), "ipodsync/tools/loadtest/adbui.mjs")));
    }
}
