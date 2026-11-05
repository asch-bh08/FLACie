using FLACie.Core;
using FLACie.Server.Dj;
using Xunit;

namespace FLACie.Tests;

/// <summary>F9: the DJ's answer as an ordered list of steps, carried out the same way by every player.</summary>
public class DjAskStepsTests
{
    sealed class FakePlayer : IDjAskPlayer
    {
        public List<string> Log = [];
        public Task PlayAsync(IReadOnlyList<string> paths, string title, bool shuffle) { Log.Add($"play {title} {paths.Count}"); return Task.CompletedTask; }
        public Task UpNextAsync(IReadOnlyList<string> paths, bool replace) { Log.Add($"upnext {string.Join(",", paths)}"); return Task.CompletedTask; }
        public Task NextAsync() { Log.Add("next"); return Task.CompletedTask; }
        public Task PauseAsync() { Log.Add("pause"); return Task.CompletedTask; }
        public Task ResumeAsync() { Log.Add("resume"); return Task.CompletedTask; }
        public Task NudgeVolumeAsync(double delta) { Log.Add("volume"); return Task.CompletedTask; }
        public Task SetDjAsync(bool on) { Log.Add($"dj {on}"); return Task.CompletedTask; }
        public Task AddToPlaylistAsync(string playlist, IReadOnlyList<string> paths) { Log.Add($"add {playlist} {string.Join(",", paths)}"); return Task.CompletedTask; }
        public Task RemoveFromPlaylistAsync(string playlist, IReadOnlyList<string> paths) { Log.Add($"remove {playlist} {string.Join(",", paths)}"); return Task.CompletedTask; }
        public Task CreatePlaylistAsync(string name, IReadOnlyList<string> paths) { Log.Add($"create {name} {paths.Count}"); return Task.CompletedTask; }
        public Task AskAsync(string question) { Log.Add($"ask {question}"); return Task.CompletedTask; }
        public Task ShuffleAsync(bool on) { Log.Add($"shuffle {on}"); return Task.CompletedTask; }
    }

    [Fact]
    public async Task Two_steps_run_in_order()
    {
        var a = new DjAskAnswer(DjAskDo.None, "Added to Pop, it's up next.")
        {
            Steps = [DjAskStep.AddTo("Pop", ["/rhythm"]), new(DjAskDo.UpNext, ["/rhythm"])],
        };
        var p = new FakePlayer();
        Assert.True(await DjAskRunner.RunAsync(p, a));
        Assert.Equal(["add Pop /rhythm", "upnext /rhythm"], p.Log);
    }

    [Fact]
    public async Task Every_kind_of_step_runs()
    {
        var a = new DjAskAnswer(DjAskDo.None, "")
        {
            Steps = [new(DjAskDo.Play, ["/a", "/b"], Title: "Pop"), DjAskStep.RemoveFrom("Pop", ["/a"]), DjAskStep.Create("Gym", ["/a", "/b"]),
                DjAskStep.Question("Which one?"), new(DjAskDo.Shuffle, Shuffle: true), new(DjAskDo.Next)],
        };
        var p = new FakePlayer();
        await DjAskRunner.RunAsync(p, a);
        Assert.Equal(["play Pop 2", "remove Pop /a", "create Gym 2", "ask Which one?", "shuffle True", "next"], p.Log);
    }

    [Fact]
    public async Task An_old_single_answer_is_one_step()
    {
        var a = new DjAskAnswer(DjAskDo.UpNext, "Next up.", ["/x"]);
        Assert.Single(a.AllSteps);
        var p = new FakePlayer();
        Assert.True(await DjAskRunner.RunAsync(p, a));
        Assert.Equal(["upnext /x"], p.Log);
    }

    [Fact]
    public async Task A_step_without_songs_is_skipped()
    {
        var a = new DjAskAnswer(DjAskDo.None, "") { Steps = [new(DjAskDo.UpNext, []), DjAskStep.AddTo("Pop", [])] };
        var p = new FakePlayer();
        Assert.False(await DjAskRunner.RunAsync(p, a));
        Assert.Empty(p.Log);
    }

    [Fact]
    public void Json_carries_the_steps_with_their_songs()
    {
        var a = new DjAskAnswer(DjAskDo.UpNext, "Added to Pop, it's up next.", ["/rhythm"])
        {
            Steps = [DjAskStep.AddTo("Pop", ["/rhythm"]), new(DjAskDo.UpNext, ["/rhythm"]), DjAskStep.Question("Shall I keep going?")],
        };
        var t = new Track("/rhythm", "Rhythm of the Night", "Corona", "A", "Corona", 1, 1, 200000, 1994, null, 1, TrackSource.Jellyfin, FilePath: "C/r.flac");
        var j = DjAskService.Json(a, x => x == "/rhythm" ? t : null);
        var s = j["steps"]!.AsArray();
        Assert.Equal(3, s.Count);
        Assert.Equal("addtoplaylist", (string?)s[0]!["do"]);
        Assert.Equal("Pop", (string?)s[0]!["playlist"]);
        Assert.Equal("Corona", (string?)s[0]!["songs"]![0]!["artist"]);
        Assert.Equal("upnext", (string?)s[1]!["do"]);
        Assert.Equal("ask", (string?)s[2]!["do"]);
        Assert.Equal("Shall I keep going?", (string?)s[2]!["say"]);
        Assert.Equal("upnext", (string?)j["do"]); // old players still read the single action
    }
}
