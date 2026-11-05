using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>F7: remix-aware matching on YouTube and SoundCloud: the remix name is kept, and the original is never swapped in.</summary>
public class RemixMatchTests
{
    [Theory]
    [InlineData("Katy Perry - Dark Horse", "Katy Perry - Topic", 215, false)]                       // the original, asked for a remix
    [InlineData("Katy Perry - Dark Horse (Official Audio)", "KatyPerryVEVO", 215, false)]
    [InlineData("Dark Horse (Kryd Remix)", "Kryd", 240, false)]                                     // another remix by the same remixer is not it either
    [InlineData("Katy Perry - Dark Horse (Kryd Hoodtrap Remix)", "Kryd", 240, true)]
    [InlineData("Dark Horse (Kryd Hoodtrap Remix)", "Kryd", 0, true)]                               // uploaded by the remixer, length unknown
    [InlineData("Katy Perry - Dark Horse (Kryd Hoodtrap Remix)", "Kryd", 300, false)]               // a different length
    public void Dark_Horse_Kryd_Hoodtrap_Remix(string got, string uploader, int sec, bool same) =>
        Assert.Equal(same, RemixMatch.Matches("Katy Perry", "Dark Horse (Kryd Hoodtrap Remix)", 240, got, uploader, sec));

    [Theory]
    [InlineData("Katy Perry - Dark Horse (Kryd Hoodtrap Remix)", false)]
    [InlineData("Katy Perry - Dark Horse - Kryd Remix", false)]
    [InlineData("Katy Perry - Dark Horse (Audio) ft. Juicy J", true)]
    [InlineData("Katy Perry - Dark Horse", true)]
    public void The_original_never_takes_a_remix(string got, bool same) =>
        Assert.Equal(same, RemixMatch.Matches("Katy Perry", "Dark Horse", 215, got, "Katy Perry", 215));

    [Theory]
    [InlineData("Dark Horse (Kryd Hoodtrap Remix)", "kryd hoodtrap remix")]
    [InlineData("Dark Horse - Kryd Hoodtrap Remix", "kryd hoodtrap remix")]
    [InlineData("Strobe (Club Edit)", "club edit")]
    [InlineData("Dark Horse (feat. Juicy J)", null)]
    [InlineData("Dark Horse (Live)", null)]
    public void The_remix_name(string title, string? remix) => Assert.Equal(remix, RemixMatch.Remix(title));
}
