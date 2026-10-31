using Xunit;

namespace FLACie.Tests;

/// <summary>The legal files the web app ships (FLACie.Server/legal: served at /licenses and /privacy) are copies of the ones at the top of the repository,
/// because the Docker image is built from ipodsync/src only and can't see those. A change to one without the other fails here.</summary>
public class LegalFilesTests
{
    static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "THIRD-PARTY-NOTICES.md"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    [Theory]
    [InlineData("LICENSE", "LICENSE.txt")]
    [InlineData("THIRD-PARTY-NOTICES.md", "THIRD-PARTY-NOTICES.md")]
    [InlineData("PRIVACY.md", "PRIVACY.md")]
    [InlineData("licenses/Apache-2.0.txt", "licenses/Apache-2.0.txt")]
    [InlineData("licenses/GPL-3.0.txt", "licenses/GPL-3.0.txt")]
    [InlineData("licenses/LGPL-2.1.txt", "licenses/LGPL-2.1.txt")]
    [InlineData("licenses/LGPL-3.0.txt", "licenses/LGPL-3.0.txt")]
    public void The_bundled_copy_matches_the_repository(string original, string bundled)
    {
        var root = Root();
        var a = File.ReadAllText(Path.Combine(root, original)).ReplaceLineEndings("\n");
        var b = File.ReadAllText(Path.Combine(root, "ipodsync", "src", "FLACie.Server", "legal", bundled)).ReplaceLineEndings("\n");
        Assert.Equal(a, b);
    }
}
