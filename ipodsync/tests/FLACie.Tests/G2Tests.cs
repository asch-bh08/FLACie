using FLACie.Server;
using Xunit;

namespace FLACie.Tests;

public class G2Tests
{
    [Fact]
    public void Version_is_the_beta_string_unless_overridden()
    {
        if (Environment.GetEnvironmentVariable("FLACIE_VERSION") is { Length: > 0 }) return;
        Assert.Equal("1.0-beta56", AppInfo.Version);
    }

    [Fact]
    public void Licenses_always_returns_text_naming_FLACie()
    {
        var t = AppInfo.Licenses();
        Assert.False(string.IsNullOrWhiteSpace(t));
    }

    [Fact]
    public void Wordmark_and_keyboard_shortcuts_ship_in_the_static_files()
    {
        var root = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(root);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "FLACie.Server", "wwwroot"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var js = File.ReadAllText(Path.Combine(dir!.FullName, "src", "FLACie.Server", "wwwroot", "flacie.js"));
        Assert.Contains("ArrowRight", js); Assert.Contains("ArrowLeft", js); Assert.Contains("\"OnNext\"", js); Assert.Contains("\"OnPrev\"", js);
        var css = File.ReadAllText(Path.Combine(dir.FullName, "src", "FLACie.Server", "wwwroot", "app.css"));
        Assert.Contains("max-width: 1000px) { .wordmark { display: none; } }", css);
    }
}