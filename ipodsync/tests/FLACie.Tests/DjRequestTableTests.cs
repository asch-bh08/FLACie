using FLACie.Server.Dj;
using Xunit;

namespace FLACie.Tests;

/// <summary>
/// The many ways people say things to the DJ (owner, 25 Nov: "make the DJ smart with talking requests"; "simple requests don't work on web like play late
/// night music"). Each line is what was heard, the action it must map to, and what it is about: [what] the song, artist, playlist, mood or version
/// (null: not checked, "-": none), [extra] the playlist for the playlist actions or the song to play after for "play X then Y". These are the quick rules
/// alone (no text model), which is what answers when the model is slow, over its cap or not set up, and on the phone's search mic.
/// </summary>
public class DjRequestTableTests
{
    public static TheoryData<string, AskIntent, string?, string?> Rows => new()
    {
        // ---- a song, a song by an artist ----
        { "play heads will roll", AskIntent.Play, "heads will roll", null },
        { "Play Heads Will Roll by Yeah Yeah Yeahs", AskIntent.Play, "heads will roll by yeah yeah yeahs", null },
        { "put on maps", AskIntent.Play, "maps", null },
        { "put maps on", AskIntent.Play, "maps", null },
        { "i want to hear maps", AskIntent.Play, "maps", null },
        { "i wanna hear maps", AskIntent.Play, "maps", null },
        { "can i hear maps", AskIntent.Play, "maps", null },
        { "let's hear maps", AskIntent.Play, "maps", null },
        { "play me maps", AskIntent.Play, "maps", null },
        { "play the song maps", AskIntent.Play, "maps", null },
        { "play the song called maps", AskIntent.Play, "maps", null },
        { "play the track zero", AskIntent.Play, "zero", null },
        { "play that song heads will roll", AskIntent.Play, "heads will roll", null },
        { "play heads will role", AskIntent.Play, "heads will role", null },
        { "play out of spaced", AskIntent.Play, "out of spaced", null },
        { "play rhythm of the night by corona", AskIntent.Play, "rhythm of the night by corona", null },
        // ---- fillers and wake words ----
        { "um can you play maps", AskIntent.Play, "maps", null },
        { "uh play maps", AskIntent.Play, "maps", null },
        { "hey DJ, play maps please", AskIntent.Play, "maps", null },
        { "hey dj um play maps", AskIntent.Play, "maps", null },
        { "dj play maps", AskIntent.Play, "maps", null },
        { "dj, can you play maps", AskIntent.Play, "maps", null },
        { "okay dj play maps", AskIntent.Play, "maps", null },
        { "yo dj put on maps", AskIntent.Play, "maps", null },
        { "so can you play maps for me", AskIntent.Play, "maps", null },
        { "could you please put on maps", AskIntent.Play, "maps", null },
        { "please play maps thank you", AskIntent.Play, "maps", null },
        { "er, play maps", AskIntent.Play, "maps", null },
        // ---- an artist, an album ----
        { "play yeah yeah yeahs", AskIntent.Play, "yeah yeah yeahs", null },
        { "play some yeah yeah yeahs", AskIntent.Play, "yeah yeah yeahs", null },
        { "play something by the prodigy", AskIntent.Play, "the prodigy", null },
        { "play songs by corona", AskIntent.Play, "corona", null },
        { "play music by corona", AskIntent.Play, "corona", null },
        { "play anything by corona", AskIntent.Play, "corona", null },
        { "shuffle the prodigy", AskIntent.Play, "the prodigy", null },
        { "play a bit of the prodigy", AskIntent.Play, "the prodigy", null },
        { "play the album the fat of the land", AskIntent.Play, "the album the fat of the land", null },
        { "play the fat of the land album", AskIntent.Play, "the fat of the land album", null },
        // ---- playlists ----
        { "play my pop playlist", AskIntent.Playlist, "pop", null },
        { "play the running playlist", AskIntent.Playlist, "running", null },
        { "put on my running playlist", AskIntent.Playlist, "running", null },
        { "shuffle my pop playlist", AskIntent.Playlist, "pop", null },
        { "play playlist pop", AskIntent.Playlist, "pop", null },
        { "play my playlist called pop", AskIntent.Playlist, "pop", null },
        { "can you play my indie playlist", AskIntent.Playlist, "indie", null },
        // ---- genres ----
        { "play some jazz", AskIntent.Mood, "jazz", null },
        { "play jazz", AskIntent.Mood, "jazz", null },
        { "play some rock music", AskIntent.Mood, null, null },
        { "play hip hop", AskIntent.Mood, "hip hop", null },
        { "put on some house music", AskIntent.Mood, null, null },
        { "i want some techno", AskIntent.Mood, null, null },
        { "play indie", AskIntent.Mood, "indie", null },
        { "play dance music", AskIntent.Mood, null, null },
        { "play some drum and bass", AskIntent.Mood, null, null },
        { "play some indie rock", AskIntent.Mood, null, null },
        // ---- moods ----
        { "play something chill", AskIntent.Mood, null, null },
        { "something chill", AskIntent.Mood, null, null },
        { "something upbeat", AskIntent.Mood, null, null },
        { "play something happy", AskIntent.Mood, null, null },
        { "play sad songs", AskIntent.Mood, null, null },
        { "play some relaxing music", AskIntent.Mood, null, null },
        { "play late night music", AskIntent.Mood, null, null },
        { "late night music", AskIntent.Mood, null, null },
        { "play something to dance to", AskIntent.Mood, null, null },
        { "play something energetic", AskIntent.Mood, null, null },
        { "chill vibes", AskIntent.Mood, null, null },
        { "play some mellow stuff", AskIntent.Mood, null, null },
        { "play something romantic", AskIntent.Mood, null, null },
        { "put on some feel good music", AskIntent.Mood, null, null },
        // ---- decades ----
        { "play some 90s music", AskIntent.Mood, null, null },
        { "play 80s music", AskIntent.Mood, null, null },
        { "play songs from the 2000s", AskIntent.Mood, null, null },
        { "play something from the nineties", AskIntent.Mood, null, null },
        { "play 90s dance", AskIntent.Mood, null, null },
        // ---- situations ----
        { "music for the gym", AskIntent.Mood, null, null },
        { "play something for the gym", AskIntent.Mood, null, null },
        { "songs for working out", AskIntent.Mood, null, null },
        { "music for a party", AskIntent.Mood, null, null },
        { "play party music", AskIntent.Mood, null, null },
        { "something for a dinner party", AskIntent.Mood, null, null },
        { "something for late at night", AskIntent.Mood, null, null },
        { "music to sleep to", AskIntent.Mood, null, null },
        { "music to study to", AskIntent.Mood, null, null },
        { "something for a rainy day", AskIntent.Mood, null, null },
        { "something for the morning", AskIntent.Mood, null, null },
        { "music for driving", AskIntent.Situation, "car", null },
        { "play my car songs", AskIntent.Situation, "car", null },
        { "something for the drive home", AskIntent.Situation, "car", null },
        // ---- more like this, less like this ----
        { "more like this", AskIntent.MoreLike, null, null },
        { "play more like this", AskIntent.MoreLike, null, null },
        { "more songs like this", AskIntent.MoreLike, null, null },
        { "play something similar", AskIntent.MoreLike, null, null },
        { "similar songs", AskIntent.MoreLike, null, null },
        { "more of this", AskIntent.MoreLike, null, null },
        { "keep this vibe going", AskIntent.MoreLike, null, null },
        { "more stuff like this", AskIntent.MoreLike, null, null },
        { "find me more like this one", AskIntent.MoreLike, null, null },
        { "less like this", AskIntent.LessLike, null, null },
        { "something different", AskIntent.LessLike, null, null },
        { "not my vibe", AskIntent.LessLike, null, null },
        { "play something else", AskIntent.LessLike, null, null },
        { "change it up", AskIntent.LessLike, null, null },
        { "im not feeling this", AskIntent.LessLike, null, null },
        // ---- which version ----
        { "the other one", AskIntent.OtherOne, "-", null },
        { "not that one", AskIntent.OtherOne, "-", null },
        { "no not that one", AskIntent.OtherOne, "-", null },
        { "wrong one", AskIntent.OtherOne, "-", null },
        { "the original", AskIntent.OtherOne, "original", null },
        { "play the original", AskIntent.OtherOne, "original", null },
        { "the original version", AskIntent.OtherOne, "original", null },
        { "not the remix", AskIntent.OtherOne, "original", null },
        { "no, not the remix, the normal one", AskIntent.OtherOne, "original", null },
        { "the album version", AskIntent.OtherOne, "original", null },
        { "the live one", AskIntent.OtherOne, "live", null },
        { "play the live version", AskIntent.OtherOne, "live", null },
        { "the remix", AskIntent.OtherOne, "remix", null },
        { "the acoustic version", AskIntent.OtherOne, "acoustic", null },
        { "the corona one", AskIntent.OtherOne, "corona", null },
        { "the one by corona", AskIntent.OtherOne, "corona", null },
        // ---- two things in a row, playlists ----
        { "play maps then heads will roll", AskIntent.Play, "maps", "heads will roll" },
        { "play maps and then heads will roll", AskIntent.Play, "maps", "heads will roll" },
        { "play maps followed by zero", AskIntent.Play, "maps", "zero" },
        { "play maps and after that play zero", AskIntent.Play, "maps", "zero" },
        { "play lovegame from my pop playlist then the rest", AskIntent.PlayFrom, "lovegame", "pop" },
        { "play maps from my indie playlist", AskIntent.PlayFrom, "maps", "indie" },
        { "add this to my pop playlist", AskIntent.AddToPlaylist, "-", "pop" },
        { "add this to pop and play it next", AskIntent.AddToPlaylist, "-", "pop" },
        { "add this to pop and play next", AskIntent.AddToPlaylist, "-", "pop" },
        { "add maps to my running playlist", AskIntent.AddToPlaylist, "maps", "running" },
        { "add this song to running", AskIntent.AddToPlaylist, "-", "running" },
        { "put this on my pop playlist", AskIntent.AddToPlaylist, "-", "pop" },
        { "save this to my pop playlist", AskIntent.AddToPlaylist, "-", "pop" },
        { "play maps and add it to my pop playlist", AskIntent.AddToPlaylist, "maps", "pop" },
        // ---- controls ----
        { "skip", AskIntent.Next, null, null },
        { "skip this", AskIntent.Next, null, null },
        { "skip this song please", AskIntent.Next, null, null },
        { "next", AskIntent.Next, null, null },
        { "next song", AskIntent.Next, null, null },
        { "can you skip", AskIntent.Next, null, null },
        { "skip it", AskIntent.Next, null, null },
        { "play the next one", AskIntent.Next, null, null },
        { "move on", AskIntent.Next, null, null },
        { "pause", AskIntent.Pause, null, null },
        { "stop the music", AskIntent.Pause, null, null },
        { "resume", AskIntent.Resume, null, null },
        { "turn it up", AskIntent.VolumeUp, null, null },
        { "turn it down", AskIntent.VolumeDown, null, null },
        // ---- what's this, who sings this ----
        { "what's this", AskIntent.WhatsThis, null, null },
        { "what song is this", AskIntent.WhatsThis, null, null },
        { "what is this song called", AskIntent.WhatsThis, null, null },
        { "what's this called", AskIntent.WhatsThis, null, null },
        { "what's playing", AskIntent.WhatsThis, null, null },
        { "what am I listening to", AskIntent.WhatsThis, null, null },
        { "what track is this", AskIntent.WhatsThis, null, null },
        { "what's the name of this song", AskIntent.WhatsThis, null, null },
        { "hey dj what's this song", AskIntent.WhatsThis, null, null },
        { "who sings this", AskIntent.WhoSings, null, null },
        { "who is this", AskIntent.WhoSings, null, null },
        { "who's singing", AskIntent.WhoSings, null, null },
        { "who is this by", AskIntent.WhoSings, null, null },
        { "who's this by", AskIntent.WhoSings, null, null },
        { "who sang this", AskIntent.WhoSings, null, null },
        { "who's the singer", AskIntent.WhoSings, null, null },
        { "who is the artist", AskIntent.WhoSings, null, null },
        { "who made this song", AskIntent.WhoSings, null, null },
    };

    [Theory, MemberData(nameof(Rows))]
    public void Each_way_of_saying_it_maps_to_the_right_action(string said, AskIntent intent, string? what, string? extra)
    {
        var p = DjAskRules.Parse(said);
        Assert.NotNull(p);
        Assert.Equal(intent, p!.Intent);
        if (what == "-") Assert.Null(p.What);
        else if (what is not null) Assert.Equal(what, p.What);
        if (extra is null) return;
        Assert.Equal(extra, intent is AskIntent.Play ? p.ThenPlay : p.Playlist);
    }

    [Fact]
    public void The_table_is_broad()
    {
        Assert.True(Rows.Count() >= 100, $"{Rows.Count()} lines");
    }
}
