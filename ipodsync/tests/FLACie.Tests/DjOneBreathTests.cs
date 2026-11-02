using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>Owner, 2 Nov (in the car): the DJ sounded pieced together, "Next" in one tone, "Without Me, Eminem" in another, then "Enjoy". A line is said as
/// one sentence: its pauses and full stops in the middle become commas, so the voice reads it in one breath.</summary>
public class DjOneBreathTests
{
    [Theory]
    [InlineData("Next. Without Me, Eminem. Enjoy.", "Next, Without Me, Eminem, enjoy.")]
    [InlineData("Okay... Without Me, by Eminem.", "Okay, Without Me, by Eminem.")]
    [InlineData("Shuffling Pop… starting with Firework, by Katy Perry.", "Shuffling Pop, starting with Firework, by Katy Perry.")]
    [InlineData("Alright, Pop on shuffle. First up, Firework by Katy Perry.", "Alright, Pop on shuffle, first up, Firework by Katy Perry.")]
    [InlineData("Here's Chandelier, from Sia", "Here's Chandelier, from Sia.")]
    [InlineData("Morning, HMS! Here's Yellow, Coldplay.", "Morning, HMS, here's Yellow, Coldplay.")]
    public void A_line_is_one_sentence(string line, string said) => Assert.Equal(said, DjText.OneBreath(line));

    [Fact]
    public void Names_keep_their_capitals_and_I_stays_I() =>
        Assert.Equal("I know, I Will Always Love You, by Whitney Houston.", DjText.OneBreath("I know. I Will Always Love You, by Whitney Houston."));

    [Fact]
    public void A_title_with_a_full_stop_inside_is_left_alone() =>
        Assert.Equal("Here's Mr. Brightside, by The Killers.", DjText.OneBreath("Here's Mr. Brightside, by The Killers."));

    [Fact]
    public void A_question_keeps_its_question_mark_at_the_end() =>
        Assert.Equal("Remember this one, Yellow, by Coldplay?", DjText.OneBreath("Remember this one? Yellow, by Coldplay?"));
}
