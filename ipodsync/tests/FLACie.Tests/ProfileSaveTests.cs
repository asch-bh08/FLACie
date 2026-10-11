using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>
/// The profile (playlists, favourites, the account) is one JsonObject shared by every task of a user: a playlist import saves it from several tasks at the
/// same time. A JsonObject read from Jellyfin is initialised lazily, and two threads doing that together wrote the same key twice, which made the saved
/// profile unreadable: the library then failed to load ("An item with the same key has already been added").
/// </summary>
public class ProfileSaveTests
{
    sealed class FakeJellyfin : HttpMessageHandler
    {
        public string Stored;
        public int Posts;
        readonly object gate = new();
        public FakeJellyfin(string profile) { Stored = profile; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var path = r.RequestUri!.AbsolutePath;
            string json;
            if (path.Contains("/DisplayPreferences/"))
            {
                if (r.Method == HttpMethod.Post)
                {
                    var dto = JsonNode.Parse(await r.Content!.ReadAsStringAsync(ct))!;
                    lock (gate) { Stored = dto["CustomPrefs"]!["ipodplayer.profile"]!.GetValue<string>(); Posts++; }
                    json = "";
                }
                else lock (gate) json = new JsonObject { ["Id"] = "ipodplayer", ["CustomPrefs"] = new JsonObject { ["ipodplayer.profile"] = Stored } }.ToJsonString();
            }
            else if (path.EndsWith("/Users/Me")) json = """{"Policy":{"IsAdministrator":false}}""";
            else json = """{"Items":[],"TotalRecordCount":0}""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    const string Remote = """{"v":2,"updated":5,"services":{},"favorites":[],"playlists":[],"deleted":[],"hidden":[]}""";

    [Fact]
    public async Task Saving_the_profile_from_many_tasks_at_once_never_writes_an_unreadable_profile()
    {
        for (var round = 0; round < 40; round++)
        {
            var fake = new FakeJellyfin(Remote);
            var jf = new JellyfinClient(new HttpClient(fake), "dev");
            var s = new UserSession("jellyfin", new JellyfinAccount("http://jf.test", "u1", "tester", "tok"), null);
            await s.LoadAsync(jf);
            Assert.True(s.ProfileLoaded);
            var gate = new ManualResetEventSlim();
            var tasks = Enumerable.Range(0, 16).Select(_ => Task.Run(async () => { gate.Wait(); await s.SaveAsync(jf); })).ToArray();
            gate.Set();
            await Task.WhenAll(tasks);
            // what the next sign-in reads back must parse and be usable
            var read = JsonNode.Parse(fake.Stored)!.AsObject();
            Assert.Equal(2, read["v"]!.GetValue<int>());
            Assert.Equal("tok", read["account"]!["token"]!.GetValue<string>());
            // and a fresh load of that profile must work
            var again = new UserSession("jellyfin", new JellyfinAccount("http://jf.test", "u1", "tester", "tok"), null);
            await again.LoadAsync(new JellyfinClient(new HttpClient(new FakeJellyfin(fake.Stored)), "dev"));
            Assert.True(again.ProfileLoaded);
        }
    }
}
