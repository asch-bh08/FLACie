using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>Names the DJ's voice mangled (owner, 9 Nov: "the DJ has a stroke trying to say Katy Perry, and names like Bobo and Gaga"): each now goes to the
/// voice with its sounds written down, inside a line as well as on its own.</summary>
public class DjNamesSaidTests
{
    [Theory]
    [InlineData("Up next, Katy Perry with Roar.", "[Katy Perry](/kˈeɪti pˈɛɹi/)")]
    [InlineData("That was Lady Gaga.", "[Lady Gaga](/lˈeɪdi ɡˈɑːɡɑː/)")]
    [InlineData("Gaga again!", "[Gaga](/ɡˈɑːɡɑː/)")]
    [InlineData("Here's Bobo.", "[Bobo](/bˈoʊboʊ/)")]
    [InlineData("Katy B is next.", "[Katy](/kˈeɪti/)")]
    public void The_name_goes_to_the_voice_with_its_sounds(string line, string marked) =>
        Assert.Contains(marked, DjPronounce.Default.Sounds(line));

    [Fact]
    public void Clips_made_before_are_made_again() => Assert.NotEqual("sounds-2", DjPronounce.SoundsVersion);
}
