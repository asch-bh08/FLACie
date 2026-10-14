using FLACie.Core;
using FLACie.Server;
using FLACie.Server.Alexa;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FLACie.Tests;

public class AlexaQueueTests
{
    static AlexaQueue Q(int n = 5, bool shuffle = false) { var q = new AlexaQueue { Rng = new Random(1) }; q.Set(Enumerable.Range(0, n).Select(i => "t" + i).ToList(), 0, shuffle); return q; }

    [Fact] public void NextWalksTheListAndStopsAtTheEnd()
    {
        var q = Q(3);
        Assert.Equal("t0", q.Current); Assert.True(q.Next()); Assert.True(q.Next()); Assert.Equal("t2", q.Current);
        Assert.False(q.Next()); Assert.Equal("t2", q.Current);
    }

    [Fact] public void RepeatAllWrapsAndRepeatOneStaysOnAutoAdvanceButSkipsOnRequest()
    {
        var q = Q(2); q.Repeat = AlexaRepeat.All; q.Next(); Assert.True(q.Next()); Assert.Equal("t0", q.Current);
        q.Repeat = AlexaRepeat.One; Assert.Equal("t0", q.PeekNext()); Assert.True(q.Next(userSkip: false)); Assert.Equal("t0", q.Current);
        Assert.True(q.Next(userSkip: true)); Assert.Equal("t1", q.Current);
    }

    [Fact] public void PreviousRestartsAfterFiveSecondsThenGoesBack()
    {
        var q = Q(3); q.Next(); q.OffsetMs = 9000;
        Assert.True(q.Previous()); Assert.Equal("t1", q.Current); Assert.Equal(0, q.OffsetMs);
        Assert.True(q.Previous()); Assert.Equal("t0", q.Current);
        Assert.True(q.Previous()); Assert.Equal("t0", q.Current);   // top of the list: restarts
    }

    [Fact] public void ShuffleKeepsTheChosenSongFirstAndHasEverySongOnce()
    {
        var q = new AlexaQueue { Rng = new Random(7) }; q.Set(["a", "b", "c", "d", "e"], 2, true);
        Assert.Equal("c", q.Current); Assert.Equal(5, q.Order.Count); Assert.Equal(["a", "b", "c", "d", "e"], q.Order.OrderBy(x => x));
    }

    [Fact] public void ShuffleOffRestoresOrderAtTheCurrentSong()
    {
        var q = Q(5, true); q.Next(); var cur = q.Current; q.SetShuffle(false);
        Assert.Equal(cur, q.Current); Assert.Equal(q.Original, q.Order);
    }

    [Fact] public void ShuffleOnMidListKeepsTheCurrentSong()
    {
        var q = Q(6); q.Next(); q.Next(); q.SetShuffle(true); Assert.Equal("t2", q.Current); Assert.Equal(0, q.Index);
    }

    [Fact] public void TokenChangesWithEveryTrackChange()
    {
        var q = Q(3); var a = q.Token; q.Next(); Assert.NotEqual(a, q.Token);
    }

    [Fact] public void InsertNextGoesRightAfterTheCurrentSong()
    {
        var q = Q(3); q.InsertNext(["x", "y"]); q.Next(); Assert.Equal("x", q.Current); q.Next(); Assert.Equal("y", q.Current);
    }

    [Fact] public void RemoveKeepsThePositionSensible()
    {
        var q = Q(4); q.Next(); q.Next(); q.Remove("t0"); Assert.Equal("t2", q.Current); q.Remove("t2"); Assert.Equal("t3", q.Current);
    }

    [Fact] public void EmptyQueueDoesNothing()
    {
        var q = new AlexaQueue(); Assert.False(q.Next()); Assert.False(q.Previous()); Assert.Null(q.Current); Assert.Null(q.PeekNext());
    }

    [Fact] public void QueuesSurviveARestart()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var a = new AlexaQueues(new DataPaths(dir)); var q = a.For("L1"); q.Set(["a", "b"], 1); q.OffsetMs = 1234; a.Save("L1");
            var b = new AlexaQueues(new DataPaths(dir)).For("L1");
            Assert.Equal("b", b.Current); Assert.Equal(1234, b.OffsetMs);
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class AlexaStoreTests
{
    sealed class Env : IDisposable
    {
        public string Dir = Directory.CreateTempSubdirectory().FullName;
        public DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
        public IDataProtectionProvider Dp => DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(Dir, "keys")));
        public AlexaStore Open(Dictionary<string, string?>? conf = null) => new(new DataPaths(Dir), Dp, new ConfigurationBuilder().AddInMemoryCollection(conf ?? []).Build(), () => Now);
        public void Dispose() { try { Directory.Delete(Dir, true); } catch (Exception) { } }
    }
    static readonly JellyfinAccount Me = new("http://jf", "u1", "ash", "SECRET-TOKEN-123");

    [Fact] public void OffByDefaultWithDownloadsOn()
    {
        using var e = new Env(); var c = e.Open().Config; Assert.False(c.Enabled); Assert.True(c.Downloads); Assert.Equal("", c.SkillIds);
    }

    [Fact] public void PairingLinksAndLookupWorks()
    {
        using var e = new Env(); var s = e.Open(); var code = s.NewCode(Me);
        Assert.Equal(6, code.Code.Length);
        Assert.False(s.IsLinked("amzn1.ask.account.X"));
        var link = s.Redeem("amzn1.ask.account.X", code.Code, "en-GB");
        Assert.NotNull(link); Assert.True(s.IsLinked("amzn1.ask.account.X"));
        Assert.Equal("SECRET-TOKEN-123", s.AccountFor("amzn1.ask.account.X")!.Token);
    }

    [Fact] public void SpokenCodeWithSpacesAndWordsOfDigitsIsRead()
    {
        using var e = new Env(); var s = e.Open(); var code = s.NewCode(Me);
        var spoken = string.Join(' ', code.Code.ToCharArray());
        Assert.NotNull(s.Redeem("U", spoken, ""));
    }

    [Fact] public void CodeWorksOnceOnly()
    {
        using var e = new Env(); var s = e.Open(); var code = s.NewCode(Me);
        Assert.NotNull(s.Redeem("A", code.Code, "")); Assert.Null(s.Redeem("B", code.Code, "")); Assert.False(s.IsLinked("B"));
    }

    [Fact] public void CodeExpiresAfterTenMinutes()
    {
        using var e = new Env(); var s = e.Open(); var code = s.NewCode(Me);
        e.Now += TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1);
        Assert.Null(s.Redeem("A", code.Code, ""));
    }

    [Fact] public void NewCodeReplacesTheEarlierOneOfTheSamePerson()
    {
        using var e = new Env(); var s = e.Open(); var a = s.NewCode(Me); var b = s.NewCode(Me);
        Assert.Null(s.Redeem("A", a.Code, "")); Assert.NotNull(s.Redeem("A", b.Code, ""));
    }

    [Fact] public void FiveWrongGuessesLockThatAlexaAccountOut()
    {
        using var e = new Env(); var s = e.Open(); var code = s.NewCode(Me);
        for (var i = 0; i < 5; i++) Assert.Null(s.Redeem("EVIL", "000000".Replace('0', (char)('1' + i)), ""));
        Assert.Null(s.Redeem("EVIL", code.Code, ""));
        Assert.NotNull(s.Redeem("GOOD", code.Code, ""));
    }

    [Fact] public void RevokeRemovesAccessAndTheSecret()
    {
        using var e = new Env(); var s = e.Open(); var code = s.NewCode(Me); var link = s.Redeem("A", code.Code, "")!;
        Assert.True(s.Revoke(link.Id)); Assert.False(s.IsLinked("A")); Assert.Null(s.AccountFor("A"));
        Assert.DoesNotContain("SECRET-TOKEN-123", File.ReadAllText(Path.Combine(e.Dir, "alexa.json")));
    }

    [Fact] public void TokenIsNeverWrittenInTheClear()
    {
        using var e = new Env(); var s = e.Open(); s.Redeem("A", s.NewCode(Me).Code, "");
        var text = File.ReadAllText(Path.Combine(e.Dir, "alexa.json"));
        Assert.DoesNotContain("SECRET-TOKEN-123", text); Assert.DoesNotContain("amzn1", text.Replace("amzn1.ask.skill", ""));
    }

    [Fact] public void LinksSurviveARestartAndPublicListHasNoSecret()
    {
        using var e = new Env(); var s = e.Open(); s.Redeem("A", s.NewCode(Me).Code, "en-US"); s.Update(c => { c.Enabled = true; c.Downloads = false; });
        var again = e.Open();
        Assert.True(again.IsLinked("A")); Assert.True(again.Config.Enabled); Assert.False(again.Config.Downloads);
        Assert.All(again.Links, l => Assert.Equal("", l.Secret));
    }

    [Fact] public void SkillIdsComeFromTheEnvironmentOnFirstStart()
    {
        using var e = new Env(); Assert.Equal("amzn1.ask.skill.abc", e.Open(new() { ["FLACIE_ALEXA_SKILL_IDS"] = "amzn1.ask.skill.abc" }).Config.SkillIds);
    }

    [Fact] public void UsersOfFindsTheAlexaAccountsOfOnePerson()
    {
        using var e = new Env(); var s = e.Open(); s.Redeem("A", s.NewCode(Me).Code, ""); s.Redeem("B", s.NewCode(Me).Code, "");
        Assert.Equal(["A", "B"], s.UsersOf("http://jf", "u1").Select(x => x.AlexaUserId).OrderBy(x => x).ToList());
    }
}

public class AlexaNowTests
{
    [Fact] public void AnEchoShowsAsADeviceOnlyWhileItHasSomethingRecent()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var me = new JellyfinAccount("http://jf", "u1", "ash", "T");
            var store = new AlexaStore(new DataPaths(dir), DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(dir, "keys"))), new ConfigurationBuilder().Build());
            var queues = new AlexaQueues(new DataPaths(dir));
            var now = new AlexaNow(store, queues);
            var s = new UserSession("jellyfin", me, null);
            var t1 = FakeUser.T("One", "Band"); var t2 = FakeUser.T("Two", "Band");
            s.Extra[t1.Path] = t1; s.Extra[t2.Path] = t2;
            Assert.Empty(now.For(s));   // Alexa is off
            store.Update(c => c.Enabled = true);
            var link = store.Redeem("A", store.NewCode(me).Code, "")!;
            Assert.Empty(now.For(s));   // nothing played yet
            var q = queues.For(link.Id); q.Set([t1.Path, t2.Path], 1); q.OffsetMs = 9000;
            var d = Assert.Single(now.For(s));
            Assert.Equal("Two", d.Current!.Title); Assert.Equal(9000, d.PositionMs); Assert.Equal(1, d.Index); Assert.Equal(2, d.Queue.Count); Assert.True(d.Playing);
            var other = new UserSession("jellyfin", new JellyfinAccount("http://jf", "u2", "bob", "T"), null);
            Assert.Empty(now.For(other));   // someone else's Echo is not shown
            q.UpdatedAt = DateTime.UtcNow - TimeSpan.FromHours(13);
            Assert.Empty(now.For(s));   // stale
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class AlexaSecurityEdgeTests
{
    [Fact] public void TheLockOutEndsAfterTenMinutesSoTyposNeverShutTheOwnerOutForGood()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var now = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
            var s = new AlexaStore(new DataPaths(dir), DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(dir, "k"))), new ConfigurationBuilder().Build(), () => now);
            var me = new JellyfinAccount("http://jf", "u1", "ash", "T");
            for (var i = 0; i < 5; i++) Assert.Null(s.Redeem("A", "11111" + i, ""));
            var code = s.NewCode(me).Code;
            Assert.Null(s.Redeem("A", code, ""));   // locked
            now += TimeSpan.FromMinutes(11);
            var fresh = s.NewCode(me).Code;
            Assert.NotNull(s.Redeem("A", fresh, ""));   // allowed again
        }
        finally { Directory.Delete(dir, true); }
    }
}
