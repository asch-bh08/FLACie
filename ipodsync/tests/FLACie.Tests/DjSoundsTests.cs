using FLACie.Core;
using FLACie.Server.Dj;
using Xunit;

namespace FLACie.Tests;

/// <summary>Names said right by FLACie's own voice (Kokoro on frank): it read "Sia" as "zhuh" and "Jay-Z" run together (owner, 30 Oct). A name with its sounds
/// written down goes to the voice as those sounds, wherever it is in what is said (lines, answers, stock replies), written or already respelt.</summary>
public class DjSoundsTests
{
    [Fact]
    public void A_known_name_goes_to_the_voice_as_its_sounds()
    {
        var p = DjPronounce.Default;
        Assert.Equal("Up next, Chandelier by [Sia](/sˈiːə/).", p.Sounds("Up next, Chandelier by Sia."));
        Assert.Equal("[Jay-Z](/dʒˈeɪ zˈiː/) and [Rihanna](/ɹiːˈænə/)", p.Sounds("Jay-Z and Rihanna"));
        Assert.Contains("[Beyoncé](/", p.Sounds("Beyoncé"));
        Assert.Contains("[beyonce](/", p.Sounds("beyonce"));
    }

    [Fact]
    public void A_name_already_respelt_for_the_old_voices_is_given_its_sounds_too()
    {
        var p = DjPronounce.Default;
        Assert.Equal("Here's [See-a](/sˈiːə/)", p.Sounds("Here's See-a"));
        Assert.Equal("[Jay Zee](/dʒˈeɪ zˈiː/)", p.Sounds("Jay Zee"));
    }

    [Fact]
    public void Words_that_merely_contain_a_name_and_text_with_none_are_left_alone()
    {
        var p = DjPronounce.Default;
        Assert.Equal("Asian and Siamese cats", p.Sounds("Asian and Siamese cats"));
        Assert.Equal("", p.Sounds(""));
        Assert.Equal("Up next, Firestarter.", p.Sounds("Up next, Firestarter."));
    }

    [Fact]
    public void Every_starter_name_has_its_sounds_in_the_voices_own_symbols()
    {
        foreach (var e in DjPronounce.StarterList)
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Ipa), e.Written);
            Assert.DoesNotContain("/", e.Ipa!); Assert.DoesNotContain("]", e.Ipa!); Assert.DoesNotContain("(", e.Ipa!);
        }
    }

    // the clips Kokoro made with the old readings are not reused once the readings change
    [Fact]
    public void A_Kokoro_clip_made_before_the_sounds_is_not_reused()
    {
        Assert.Contains(DjPronounce.SoundsVersion, DjClips.Salt("kokoro:am_liam"));
        Assert.Equal("", DjClips.Salt("aura-1:helios"));
    }

    // brackets in a title are a short pause, not words run together ("Tonight (Dyia)" was said as one breathless phrase)
    [Fact]
    public void Brackets_left_in_a_title_become_a_pause()
    {
        Assert.Equal("Hold On, Change Your Mind", DjText.Speak("Hold On (Change Your Mind)"));
        Assert.Equal("Tonight, Dyia", DjText.Speak("Tonight (Dyia)"));
        Assert.Equal("Hello, World, Again", DjText.Speak("Hello [World] Again"));
    }
}
