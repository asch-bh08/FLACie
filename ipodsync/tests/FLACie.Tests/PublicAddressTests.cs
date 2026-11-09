using FLACie.Core;
using FLACie.Server;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FLACie.Tests;

/// <summary>Owner, 9 Nov: the phone's FLACie Web address followed whichever address the web page was last opened at (the server's own, a proxy, a LAN address),
/// so the phone could lose the server away from home. One stable address now: set in Server admin or FLACIE_PUBLIC_URL, else the first tailnet address seen.</summary>
public class PublicAddressTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("flacie-addr").FullName;
    public void Dispose() { try { Directory.Delete(dir, true); } catch (Exception) { } }
    static IConfiguration Conf(string? url = null) => new ConfigurationBuilder().AddInMemoryCollection(url is null ? [] : new Dictionary<string, string?> { ["FLACIE_PUBLIC_URL"] = url }).Build();
    PublicAddress Make(string? env = null) => new(new DataPaths(dir), Conf(env));

    [Fact]
    public void A_lan_or_local_page_address_is_never_handed_to_the_phone()
    {
        var a = Make();
        Assert.Null(a.Use("http://192.168.1.20:8443"));
        Assert.Null(a.Use("http://localhost:5290"));
        Assert.Null(a.Use("http://127.0.0.1:5290"));
        Assert.Null(a.Current);
    }

    [Fact]
    public void The_first_tailnet_address_seen_is_kept_and_a_later_other_address_does_not_move_it()
    {
        var a = Make();
        Assert.Equal("https://home.example.ts.net:8443", a.Use("https://home.example.ts.net:8443"));
        // opened later through a proxy or on the LAN: the phone keeps the first one
        Assert.Equal("https://home.example.ts.net:8443", a.Use("https://music.example.ts.net"));
        Assert.Equal("https://home.example.ts.net:8443", a.Use("http://192.0.2.10:8443"));
        // and after a restart
        Assert.Equal("https://home.example.ts.net:8443", Make().Current);
        Assert.Contains("first", Make().Source);
    }

    [Fact]
    public void A_tailnet_ip_counts_as_a_tailnet_address()
    {
        // made-up addresses built from their numbers (no real address is written in a test file): one inside Tailscale's range, one just outside it
        static Uri At(byte a, byte b) => new($"http://{new System.Net.IPAddress(new byte[] { a, b, 7, 9 })}:8443");
        Assert.True(PublicAddress.IsTailnet(At(100, 101)));
        Assert.False(PublicAddress.IsTailnet(At(100, 200)));   // just outside Tailscale's range
        Assert.True(PublicAddress.IsTailnet(new Uri("https://home.example.ts.net")));
        Assert.False(PublicAddress.IsTailnet(new Uri("http://192.0.2.10")));
    }

    [Fact]
    public void The_address_set_in_Server_admin_wins_and_clearing_it_goes_back_to_the_one_seen()
    {
        var a = Make();
        a.Use("https://home.example.ts.net:8443");
        Assert.True(a.Set("https://flacie.example.ts.net/"));
        Assert.Equal("https://flacie.example.ts.net", a.Current);
        Assert.Equal("https://flacie.example.ts.net", a.Use("https://home.example.ts.net:8443"));
        Assert.Equal("https://flacie.example.ts.net", Make().Current);
        Assert.Contains("Server admin", Make().Source);
        Assert.False(a.Set("ftp://nope.example.ts.net"));
        Assert.False(a.Set("not an address"));
        Assert.Equal("https://flacie.example.ts.net", a.Current);
        Assert.True(a.Set(""));
        Assert.Equal("https://home.example.ts.net:8443", a.Current);
    }

    [Fact]
    public void FLACIE_PUBLIC_URL_wins_over_everything()
    {
        var a = Make("https://env.example.ts.net:8443/");
        a.Set("https://flacie.example.ts.net");
        Assert.Equal("https://env.example.ts.net:8443", a.Current);
        Assert.Equal("https://env.example.ts.net:8443", a.Use("http://192.0.2.10:8443"));
        Assert.Contains("FLACIE_PUBLIC_URL", a.Source);
        Assert.True(a.FromEnvironment);
    }
}
