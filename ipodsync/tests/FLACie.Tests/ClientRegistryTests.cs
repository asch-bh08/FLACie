using FLACie.Core;
using FLACie.Server;
using Microsoft.JSInterop;
using Xunit;

namespace FLACie.Tests;

/// <summary>Owner, 9 Nov: Server admin showed three "Edge on Windows" for one person. A closed or reloaded tab is kept for 30 minutes in case it
/// comes back (a phone in the background); the dashboard lists only the pages connected now.</summary>
public class ClientRegistryTests
{
    sealed class Js : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => ValueTask.FromResult(default(T)!);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<T>(identifier, args);
    }

    [Fact]
    public void Only_connected_pages_are_open()
    {
        var reg = new ClientRegistry();
        var me = new UserSession("jellyfin", new JellyfinAccount("http://192.0.2.10", "u1", "tester", "tok"), null) { Id = "u1" };
        var live = new CircuitPresence(); var gone = new CircuitPresence();
        IPlayerClients r = reg;
        r.Add("a", me, new PlayerState(new Js()), "192.0.2.5", "", "Edge", "Windows", "", "", gone);
        r.Add("b", me, new PlayerState(new Js()), "192.0.2.5", "", "Edge", "Windows", "", "", live);
        gone.Up = false;
        Assert.Equal(["b"], reg.Open.Select(c => c.Id));
        Assert.Equal(2, reg.All.Count);
        gone.Up = true;
        Assert.Equal(2, reg.Open.Count);
    }
}
