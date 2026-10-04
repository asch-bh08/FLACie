using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FLACie.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace FLACie.Server;

/// <summary>
/// Who is signed in. The sign-in cookie carries the account (a Jellyfin token, or the NAS login), encrypted with the
/// server's data-protection keys, so nothing usable reaches the browser and a restart doesn't sign anyone out.
/// One <see cref="UserSession"/> per account is kept in memory and shared by that user's tabs.
/// </summary>
public sealed record Live(Connect Connect, Jam Jam);

public sealed class SessionStore(JellyfinClient jf, ILogger<SessionStore> log, ImportManager imports, UserStateStore states)
{
    readonly ConcurrentDictionary<string, UserSession> sessions = new();
    /// <summary>Every account with a session on this server right now.</summary>
    public IEnumerable<UserSession> Active => sessions.Values;

    public static ClaimsPrincipal Principal(JellyfinAccount? j, NasAccount? n)
    {
        var c = new List<Claim> { new("kind", j is not null ? "jellyfin" : "nas") };
        if (j is not null) c.AddRange([new("jf.server", j.Server), new("jf.user", j.UserId), new("jf.name", j.UserName), new("jf.token", j.Token), new(ClaimTypes.Name, j.UserName)]);
        if (n is not null) c.AddRange([new("nas.host", n.Host), new("nas.share", n.Share), new("nas.folder", n.Folder), new("nas.user", n.User), new("nas.pass", n.Password), new("nas.domain", n.Domain)]);
        if (j is null && n is not null) c.Add(new(ClaimTypes.Name, n.User.Length == 0 ? "guest" : n.User));
        return new ClaimsPrincipal(new ClaimsIdentity(c, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    static string Key(ClaimsPrincipal p)
    {
        var raw = p.FindFirst("kind")?.Value == "jellyfin"
            ? $"jf|{p.FindFirst("jf.server")?.Value}|{p.FindFirst("jf.user")?.Value}"
            : $"nas|{p.FindFirst("nas.host")?.Value}|{p.FindFirst("nas.share")?.Value}|{p.FindFirst("nas.user")?.Value}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    /// <summary>This user's session, loading the library the first time.</summary>
    public UserSession For(ClaimsPrincipal p)
    {
        var s = sessions.GetOrAdd(Key(p), key =>
        {
            var kind = p.FindFirst("kind")?.Value ?? "jellyfin";
            JellyfinAccount? j = p.FindFirst("jf.token") is { } t ? new(p.FindFirst("jf.server")!.Value, p.FindFirst("jf.user")!.Value, p.FindFirst("jf.name")?.Value ?? "", t.Value) : null;
            NasAccount? n = p.FindFirst("nas.host") is { } h ? new(h.Value, V(p, "nas.share"), V(p, "nas.folder"), V(p, "nas.user"), V(p, "nas.pass"), V(p, "nas.domain")) : null;
            var us = new UserSession(kind, j, n) { Id = key }; states.Remember(us);
            _ = Task.Run(async () => { try { await us.LoadAsync(jf); } catch (Exception e) { log.LogWarning(e, "Loading library failed"); } imports.Resume(us); });
            return us;
        });
        return s;
    }

    /// <summary>Connect and the Jam for a user who signed in with Jellyfin (null for a NAS sign-in: its token isn't this server's device).</summary>
    public Live? LiveFor(UserSession s)
    {
        if (s.Kind != "jellyfin" || s.Jellyfin is null) return null;
        return live.GetValue(s, u => { var c = new Connect(jf, u); var l = new Live(c, new Jam(c)); c.Start(); return l; });
    }

    readonly System.Runtime.CompilerServices.ConditionalWeakTable<UserSession, Live> live = new();

    public void Forget(ClaimsPrincipal p)
    {
        if (sessions.TryRemove(Key(p), out var s) && live.TryGetValue(s, out var l)) { l.Connect.Dispose(); live.Remove(s); }
    }
    static string V(ClaimsPrincipal p, string k) => p.FindFirst(k)?.Value ?? "";
}

public static class AuthEndpoints
{
    public static void MapAuth(this WebApplication app)
    {
        var allowNas = app.Configuration.GetValue("FLACIE_ALLOW_NAS_LOGIN", true);
        var fixedServer = app.Configuration["FLACIE_JELLYFIN_URL"];

        async Task SignIn(HttpContext ctx, JellyfinAccount? j, NasAccount? n) =>
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, SessionStore.Principal(j, n),
                new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30) });

        static IResult Back(string error, string tab = "") => Results.Redirect("/login?error=" + Uri.EscapeDataString(error) + (tab.Length > 0 ? "&tab=" + tab : ""));

        app.MapPost("/auth/jellyfin", async (HttpContext ctx, JellyfinClient jf) =>
        {
            var f = ctx.Request.Form;
            var server = fixedServer ?? f["server"].ToString();
            if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(f["user"])) return Back("Enter the server and your username.");
            try { await SignIn(ctx, await jf.SignInAsync(server, f["user"].ToString().Trim(), f["password"].ToString()), null); return Results.Redirect("/"); }
            catch (UnauthorizedAccessException) { return Back("Wrong username or password."); }
            catch (Exception e) { return Back($"Couldn't reach {server}: {e.Message}"); }
        }).DisableAntiforgery();

        app.MapPost("/auth/nas", async (HttpContext ctx) =>
        {
            if (!allowNas) return Back("NAS sign-in is turned off on this server.");
            var f = ctx.Request.Form;
            var n = new NasAccount(f["host"].ToString().Trim(), f["share"].ToString().Trim(), f["folder"].ToString().Trim('/', ' '), f["user"].ToString().Trim(), f["password"].ToString());
            if (n.Host.Length == 0 || n.Share.Length == 0) return Back("Enter the NAS address and share.", "nas");
            try { await Task.Run(() => NasClient.Test(n)); await SignIn(ctx, null, n); return Results.Redirect("/"); }
            catch (Exception e) { return Back(e.Message, "nas"); }
        }).DisableAntiforgery();

        // Quick Connect: the page polls until the code is approved in another Jellyfin app, then this signs in
        app.MapPost("/auth/qc/start", async (QcStart body, JellyfinClient jf) =>
        {
            var server = fixedServer ?? body.Server;
            try { var (code, secret) = await jf.QuickConnectStartAsync(server); return Results.Ok(new { code, secret, server = JellyfinClient.Normalise(server) }); }
            catch (Exception e) { return Results.BadRequest(new { error = $"Quick Connect isn't available on {server}: {e.Message}" }); }
        }).DisableAntiforgery();

        app.MapPost("/auth/qc/poll", async (HttpContext ctx, QcPoll body, JellyfinClient jf) =>
        {
            try
            {
                var a = await jf.QuickConnectPollAsync(fixedServer ?? body.Server, body.Secret);
                if (a is null) return Results.Ok(new { done = false });
                await SignIn(ctx, a, null);
                return Results.Ok(new { done = true });
            }
            catch (Exception e) { return Results.BadRequest(new { error = e.Message }); }
        }).DisableAntiforgery();

        app.MapPost("/auth/logout", async (HttpContext ctx, SessionStore store, JellyfinClient jf) =>
        {
            var s = store.For(ctx.User);
            if (s.Jellyfin is { } a && s.Kind == "jellyfin") try { await jf.SignOutAsync(a); } catch { }
            store.Forget(ctx.User);
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login");
        }).DisableAntiforgery();
    }

    public sealed record QcStart(string Server);
    public sealed record QcPoll(string Server, string Secret);
}
