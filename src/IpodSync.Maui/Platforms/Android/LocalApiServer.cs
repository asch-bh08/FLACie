using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using IpodSync.Shared.Backend;

namespace IpodSync.Maui.Platforms.Android;

/// <summary>
/// The same small JSON API IpodSync.Web serves on a PC (<c>/api/devices</c>, <c>/api/library</c>,
/// <c>/api/apply-edits</c>), served by this app on the phone itself so ipodplayer's Sync mode can edit an iPod
/// plugged into the phone over USB-OTG -- no PC and no Wi-Fi. It is backed by <see cref="AndroidIpodSyncBackend"/>,
/// i.e. the same verified WritePipeline this app's own UI uses (backup, write, read-back, re-verify, auto-restore),
/// and writes follow the same <see cref="RemoteEdits"/> rules as the PC (op allow-list, dry-run confirm token).
///
/// Bound to 127.0.0.1 only: nothing on the network can reach it, only apps on this phone. Plain HTTP/1.1, one
/// request per connection -- all ipodplayer needs.
/// </summary>
public static class LocalApiServer
{
    public const int Port = 5071;
    static TcpListener? _listener;

    public static void Start(IIpodSyncBackend backend)
    {
        if (_listener != null) return;
        try
        {
            _listener = new TcpListener(IPAddress.Loopback, Port);
            _listener.Start();
        }
        catch (Exception) { _listener = null; return; }   // port taken (another copy running): ipodplayer just finds that one
        _ = Task.Run(async () =>
        {
            while (_listener != null)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(); } catch { break; }
                _ = Task.Run(() => HandleAsync(client, backend));
            }
        });
    }

    static async Task HandleAsync(TcpClient client, IIpodSyncBackend backend)
    {
        using var _ = client;
        using var stream = client.GetStream();
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));   // a real write (backup + verify) takes a while
        int status; object body;
        try
        {
            var (method, target, content) = await ReadRequestAsync(stream, cts.Token);
            var uri = new Uri("http://localhost" + target);
            var q = System.Web.HttpUtility.ParseQueryString(uri.Query);
            string? root = q["root"];
            (status, body) = (method, uri.AbsolutePath) switch
            {
                ("GET", "/api/devices") => (200, await backend.DetectDevicesAsync(cts.Token)),
                ("GET", "/api/library") when !string.IsNullOrEmpty(root) => (200, (object)await backend.LoadLibraryAsync(root, cts.Token)),
                ("POST", "/api/apply-edits") when !string.IsNullOrEmpty(root) =>
                    await RemoteEdits.HandleAsync(backend, root, q["commit"] == "1", q["confirm"], content, cts.Token),
                _ => (404, new { error = $"no route for {method} {uri.AbsolutePath}" }),
            };
        }
        catch (Exception ex) { status = 500; body = new { error = ex.Message, detail = ex.Message }; }

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(body, body.GetType(), RemoteEdits.Json);
        string head = $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Error")}\r\nContent-Type: application/json; charset=utf-8\r\n" +
                      $"Content-Length: {json.Length}\r\nConnection: close\r\n\r\n";
        try
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
            await stream.WriteAsync(json);
        }
        catch { /* client went away */ }
    }

    static async Task<(string Method, string Target, string Body)> ReadRequestAsync(NetworkStream s, CancellationToken ct)
    {
        var buf = new List<byte>(4096);
        var one = new byte[4096];
        int headerEnd = -1;
        while (headerEnd < 0)
        {
            int n = await s.ReadAsync(one, ct);
            if (n <= 0) throw new IOException("connection closed");
            buf.AddRange(one.AsSpan(0, n).ToArray());
            if (buf.Count > 64 * 1024) throw new IOException("headers too large");
            for (int i = 3; i < buf.Count; i++)
                if (buf[i - 3] == '\r' && buf[i - 2] == '\n' && buf[i - 1] == '\r' && buf[i] == '\n') { headerEnd = i + 1; break; }
        }
        var lines = Encoding.ASCII.GetString(buf.GetRange(0, headerEnd).ToArray()).Split("\r\n");
        var parts = lines[0].Split(' ');
        if (parts.Length < 2) throw new IOException("bad request line");
        int length = 0;
        foreach (var l in lines)
            if (l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) int.TryParse(l[15..].Trim(), out length);
        if (length > 4 * 1024 * 1024) throw new IOException("body too large");
        var body = buf.GetRange(headerEnd, buf.Count - headerEnd);
        while (body.Count < length)
        {
            int n = await s.ReadAsync(one, ct);
            if (n <= 0) break;
            body.AddRange(one.AsSpan(0, n).ToArray());
        }
        return (parts[0], parts[1], Encoding.UTF8.GetString(body.ToArray(), 0, Math.Min(length, body.Count)));
    }
}
