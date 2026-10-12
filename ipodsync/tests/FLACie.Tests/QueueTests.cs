using System.Text.Json;
using FLACie.Core;
using FLACie.Server;
using Microsoft.JSInterop;
using Xunit;

namespace FLACie.Tests;

/// <summary>Editing Up next: the pure arithmetic (same semantics as the Android app's QueueEdit), the player's use of it, what is remembered across a reload, and the client label.</summary>
public class QueueTests
{
    static readonly int[] Seq = [0, 1, 2, 3, 4, 5];

    [Fact]
    public void Upcoming_is_what_follows_the_current_song_and_with_repeat_all_the_ones_before_after_the_wrap()
    {
        Assert.Equal([3, 4, 5], QueueEdit.Upcoming(Seq, 2, false));
        Assert.Equal([3, 4, 5, 0, 1], QueueEdit.Upcoming(Seq, 2, true));
        Assert.Empty(QueueEdit.Upcoming(Seq, 5, false));
        Assert.Empty(QueueEdit.Upcoming(Seq, 99, true));
        Assert.Equal([0, 5, 1], QueueEdit.Upcoming([2, 0, 5, 1], 2, false));   // a shuffled order
    }

    [Fact]
    public void Moving_an_upcoming_song_changes_only_the_upcoming_slots_and_never_the_current_one()
    {
        Assert.Equal([0, 1, 2, 4, 3, 5], QueueEdit.Reorder(Seq, 2, false, 4, QueueEdit.Step.Up));
        Assert.Equal([0, 1, 2, 3, 5, 4], QueueEdit.Reorder(Seq, 2, false, 4, QueueEdit.Step.Down));
        Assert.Equal([0, 1, 2, 5, 3, 4], QueueEdit.Reorder(Seq, 2, false, 5, QueueEdit.Step.Top));
        // the first can't go up or to the top, the last can't go down, songs already played and the current one are not "upcoming"
        Assert.Null(QueueEdit.Reorder(Seq, 2, false, 3, QueueEdit.Step.Up));
        Assert.Null(QueueEdit.Reorder(Seq, 2, false, 3, QueueEdit.Step.Top));
        Assert.Null(QueueEdit.Reorder(Seq, 2, false, 5, QueueEdit.Step.Down));
        Assert.Null(QueueEdit.Reorder(Seq, 2, false, 1, QueueEdit.Step.Down));
        Assert.Null(QueueEdit.Reorder(Seq, 2, false, 2, QueueEdit.Step.Up));
    }

    [Fact]
    public void Moving_works_in_a_shuffled_play_order_and_across_the_repeat_all_wrap()
    {
        int[] shuffled = [3, 0, 5, 1, 4, 2];                         // current 3 plays first, then 0 5 1 4 2
        Assert.Equal([3, 5, 0, 1, 4, 2], QueueEdit.Reorder(shuffled, 3, false, 5, QueueEdit.Step.Top));
        // current 5 in the middle of [0 1 2 5 3 4]: with repeat-all 3 4 0 1 2 follow
        int[] o = [0, 1, 2, 5, 3, 4];
        Assert.Equal([4, 0, 1, 5, 2, 3], QueueEdit.Reorder(o, 5, true, 2, QueueEdit.Step.Top));
        var wrapped = QueueEdit.Reorder(o, 5, true, 1, QueueEdit.Step.Top)!;
        Assert.Equal([4, 0, 2, 5, 1, 3], wrapped);
        Assert.Equal(5, wrapped[3]);                                  // the current song keeps its own slot
        Assert.Equal([1, 3, 4, 0, 2], QueueEdit.Upcoming(wrapped, 5, true));
    }

    [Fact]
    public void Taking_out_a_song_before_or_after_the_current_one_never_changes_what_plays()
    {
        var r = QueueEdit.Remove(Seq, 3, 1, false)!;                  // before: the current index shifts down
        Assert.Equal([0, 1, 2, 3, 4], r.Order); Assert.Equal(2, r.Current); Assert.False(r.CurrentRemoved);
        r = QueueEdit.Remove(Seq, 3, 5, false)!;                      // after
        Assert.Equal([0, 1, 2, 3, 4], r.Order); Assert.Equal(3, r.Current); Assert.False(r.CurrentRemoved);
        Assert.Null(QueueEdit.Remove(Seq, 3, 9, false));
    }

    [Fact]
    public void Taking_out_the_playing_song_starts_the_one_that_follows_and_at_the_end_stops_on_the_one_before()
    {
        var r = QueueEdit.Remove(Seq, 2, 2, false)!;
        Assert.Equal([0, 1, 2, 3, 4], r.Order); Assert.Equal(2, r.Current); Assert.True(r.CurrentRemoved); Assert.False(r.Stopped);   // old 3 is now index 2
        // shuffled: 4 plays after 1
        int[] sh = [1, 4, 0, 3, 2, 5];
        r = QueueEdit.Remove(sh, 1, 1, false)!;
        Assert.Equal(3, r.Current);                                   // old 4 -> 3 after the shift
        Assert.Equal([3, 0, 2, 1, 4], r.Order);
        // last in play order, no repeat: the song before it becomes current and playback stops there
        r = QueueEdit.Remove(Seq, 5, 5, false)!;
        Assert.Equal(4, r.Current); Assert.True(r.CurrentRemoved); Assert.True(r.Stopped);
        // last with repeat-all: the first one takes over and plays
        r = QueueEdit.Remove(Seq, 5, 5, true)!;
        Assert.Equal(0, r.Current); Assert.False(r.Stopped);
        // the only song
        r = QueueEdit.Remove([0], 0, 0, false)!;
        Assert.Empty(r.Order); Assert.Equal(-1, r.Current); Assert.True(r.Stopped);
    }

    [Fact]
    public void A_song_put_next_goes_right_after_the_current_one_in_play_order_whatever_that_order_is()
    {
        var (o, c) = QueueEdit.InsertNext(Seq, 2, 3);                  // inserted into the queue at index 3
        Assert.Equal([0, 1, 2, 3, 4, 5, 6], o); Assert.Equal(2, c);
        (o, c) = QueueEdit.InsertNext([3, 0, 5, 1, 4, 2], 3, 4);       // shuffled: after 3 comes the new one (4); the old 4 is 5 now
        Assert.Equal([3, 4, 0, 6, 1, 5, 2], o); Assert.Equal(3, c);
        (o, c) = QueueEdit.InsertNext([0, 1, 2], 0, 1);
        Assert.Equal([0, 1, 2, 3], o); Assert.Equal(0, c);
    }

    [Fact]
    public void Any_run_of_edits_keeps_a_valid_play_order_and_moves_never_change_the_current_song()
    {
        var rnd = new Random(7);
        for (var round = 0; round < 200; round++)
        {
            var n = rnd.Next(2, 12);
            var order = Enumerable.Range(0, n).OrderBy(_ => rnd.Next()).ToList();
            var current = order[rnd.Next(n)];
            for (var k = 0; k < 20; k++)
            {
                var item = rnd.Next(n);
                var step = (QueueEdit.Step)rnd.Next(3);
                var before = order.IndexOf(current);
                if (QueueEdit.Reorder(order, current, false, item, step) is { } moved)
                {
                    Assert.Equal(order.Count, moved.Count);
                    Assert.Equal(Enumerable.Range(0, n), moved.OrderBy(x => x));
                    Assert.Equal(current, moved[before]);            // the current song keeps its slot
                    Assert.Equal(order.Take(before + 1), moved.Take(before + 1));   // and so does everything up to it
                    order = moved;
                }
            }
        }
    }

    // ---- the player ----

    sealed class FakeJs : IJSRuntime
    {
        public readonly List<(string Id, object?[] Args)> Calls = [];
        public string? Unstash;
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args)
        {
            Calls.Add((identifier, args ?? []));
            if (identifier == "flacie.unstash" && Unstash is not null) return ValueTask.FromResult((T)(object)Unstash);
            return ValueTask.FromResult(default(T)!);
        }
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<T>(identifier, args);
        public string? LastStash() => Calls.LastOrDefault(c => c.Id == "flacie.stash" && c.Args.Length > 1 && c.Args[0] is string s && s.StartsWith("flacie.queue.")).Args?.ElementAtOrDefault(1) as string;
        public string? LastLoadedPath() => Calls.LastOrDefault(c => c.Id == "flacie.load").Args?.ElementAtOrDefault(1) as string;
        public int Loads => Calls.Count(c => c.Id == "flacie.load");
    }

    static Track T(int n) => new($"/m/{n}.flac", $"Song {n}", "A", "Al", "A", n, 1, 100_000, 2000, null, 0, TrackSource.Jellyfin, $"/m/{n}.flac", n.ToString("x32"));
    static async Task<(PlayerState P, FakeJs Js)> Player(int songs, int start = 0, bool shuffle = false)
    {
        var js = new FakeJs(); var p = new PlayerState(js);
        p.SetStashKey("u");
        if (shuffle) p.ToggleShuffle();
        await p.Play(Enumerable.Range(0, songs).Select(T).ToList(), start);
        return (p, js);
    }
    static string[] Next(PlayerState p) => p.UpNext(100).Select(x => x.Track.Title).ToArray();

    [Fact]
    public async Task Up_next_rows_move_up_down_and_to_the_top_and_the_current_song_is_untouched()
    {
        var (p, js) = await Player(6, 1);                             // playing Song 1; Up next: 2 3 4 5
        Assert.Equal(["Song 2", "Song 3", "Song 4", "Song 5"], Next(p));
        p.MoveUpNext(4, QueueEdit.Step.Up);
        Assert.Equal(["Song 2", "Song 4", "Song 3", "Song 5"], Next(p));
        p.MoveUpNext(2, QueueEdit.Step.Down);
        Assert.Equal(["Song 4", "Song 2", "Song 3", "Song 5"], Next(p));
        p.MoveUpNext(5, QueueEdit.Step.Top);
        Assert.Equal(["Song 5", "Song 4", "Song 2", "Song 3"], Next(p));
        Assert.Equal("Song 1", p.Current!.Title);
        Assert.Equal(1, js.Loads);                                    // nothing was loaded again: the song kept playing
        p.MoveUpNext(5, QueueEdit.Step.Up);                           // already first: nothing
        Assert.Equal(["Song 5", "Song 4", "Song 2", "Song 3"], Next(p));
        // Next really goes to the moved song
        await p.NextAsync();
        Assert.Equal("Song 5", p.Current!.Title);
        Assert.Equal(["Song 4", "Song 2", "Song 3"], Next(p));
    }

    [Fact]
    public async Task Removing_an_upcoming_song_leaves_what_is_playing_alone_and_removing_the_playing_one_starts_the_next()
    {
        var (p, js) = await Player(6, 1);
        await p.RemoveFromQueue(3);                                   // Song 3, upcoming
        Assert.Equal(["Song 2", "Song 4", "Song 5"], Next(p));
        Assert.Equal("Song 1", p.Current!.Title); Assert.Equal(1, js.Loads);
        await p.RemoveFromQueue(0);                                   // Song 0, already played: the current index shifts, the song does not change
        Assert.Equal("Song 1", p.Current!.Title); Assert.Equal(1, js.Loads);
        Assert.Equal(["Song 2", "Song 4", "Song 5"], Next(p));
        await p.RemoveFromQueue(p.Index);                             // the playing song
        Assert.Equal("Song 2", p.Current!.Title);
        Assert.Equal(2, js.Loads);
        Assert.Equal("/stream?p=" + Uri.EscapeDataString("/m/2.flac"), js.LastLoadedPath());
        Assert.Equal(["Song 4", "Song 5"], Next(p));
        // out to the end: the last song leaves, the one before it waits paused
        await p.RemoveFromQueue(p.Queue.FindIndex(t => t.Title == "Song 4"));
        await p.RemoveFromQueue(p.Queue.FindIndex(t => t.Title == "Song 5"));
        await p.NextAsync();
        Assert.Empty(Next(p));
    }

    [Fact]
    public async Task The_last_song_taken_out_closes_the_player()
    {
        var (p, _) = await Player(1);
        await p.RemoveFromQueue(0);
        Assert.Null(p.Current); Assert.Empty(p.Queue);
    }

    [Fact]
    public async Task Play_next_puts_a_song_right_after_the_current_one_even_in_an_arranged_or_shuffled_queue()
    {
        var (p, _) = await Player(5, 0);
        p.MoveUpNext(4, QueueEdit.Step.Top);                          // arranged: 4 1 2 3
        p.AddNext(T(9));
        Assert.Equal(["Song 9", "Song 4", "Song 1", "Song 2", "Song 3"], Next(p));
        Assert.Equal("Song 0", p.Current!.Title);
        var (s, _) = await Player(8, 3, shuffle: true);
        var cur = s.Current!.Title;
        s.AddNext(T(20));
        Assert.Equal("Song 20", Next(s)[0]);
        Assert.Equal(cur, s.Current!.Title);
        Assert.Equal(9, s.Queue.Count);
    }

    [Fact]
    public async Task The_arranged_order_is_remembered_and_comes_back_after_a_reload_even_when_shuffled()
    {
        foreach (var shuffle in new[] { false, true })
        {
            var (p, js) = await Player(7, 2, shuffle);
            var now = p.Current!.Title;
            var upcoming = Next(p);
            p.MoveUpNext(p.UpNext(1).Skip(0).First().Index, QueueEdit.Step.Down);
            var arranged = Next(p);
            Assert.NotEqual(upcoming, arranged);
            await p.RemoveFromQueue(p.UpNext(3).Last().Index);
            arranged = Next(p);
            var json = js.LastStash();
            Assert.NotNull(json);
            // a new tab (a reload) restores it
            var js2 = new FakeJs { Unstash = json }; var p2 = new PlayerState(js2); p2.SetStashKey("u");
            var s = new UserSession("jellyfin", null, null);
            s.AddDownloaded(p.Queue);
            await p2.RestoreAsync(s);
            Assert.Equal(now, p2.Current!.Title);
            Assert.Equal(arranged, Next(p2));
        }
    }

    // ---- the client label ----

    [Theory]
    [InlineData("Edge", "Windows", true, true, "FLACie for Windows")]       // the app's own WebView2
    [InlineData("Edge", "Windows", false, true, "Edge on Windows")]          // the same browser on a normal server
    [InlineData("Chrome", "Windows", true, true, "Chrome on Windows")]       // another browser pointed at the app's server
    [InlineData("Edge", "Windows", true, false, "Edge on Windows")]          // from another address
    [InlineData("Firefox", "", false, false, "Firefox")]
    public void The_embedded_view_of_the_desktop_app_is_the_app_and_nothing_else_is_renamed(string browser, string os, bool shell, bool loopback, string label) =>
        Assert.Equal(label, ClientLabel.Of(browser, os, shell, loopback));

    [Fact]
    public void The_jellyfin_device_name_is_shown_as_the_app_only_for_this_player_inside_the_app()
    {
        Assert.Equal("FLACie for Windows", ClientLabel.Device("FLACie Web", true, true));
        Assert.Equal("FLACie Web", ClientLabel.Device("FLACie Web", true, false));
        Assert.Equal("FLACie Web", ClientLabel.Device("FLACie Web", false, true));      // another FLACie Web session
        Assert.Equal("Pixel 8", ClientLabel.Device("Pixel 8", true, true));
        // what is sent to Jellyfin does not change
        Assert.Equal("ipodplayer", JellyfinClient.Client);
        Assert.Equal("ipodplayer", JellyfinClient.ClientName);
    }

    [Fact]
    public void A_web_client_reports_the_label_for_its_address_and_the_desktop_shell_setting()
    {
        var old = Environment.GetEnvironmentVariable("FLACIE_PARENT_PID");
        try
        {
            var c = new WebClient("1", new UserSession("jellyfin", null, null), new PlayerState(new FakeJs())) { Browser = "Edge", Os = "Windows", Ip = "127.0.0.1" };
            Environment.SetEnvironmentVariable("FLACIE_PARENT_PID", null);
            Assert.Equal("Edge on Windows", c.Label);
            Environment.SetEnvironmentVariable("FLACIE_PARENT_PID", "4242");
            Assert.Equal("FLACie for Windows", c.Label);
            c.Ip = "192.168.1.20";
            Assert.Equal("Edge on Windows", c.Label);
        }
        finally { Environment.SetEnvironmentVariable("FLACIE_PARENT_PID", old); }
    }
}
