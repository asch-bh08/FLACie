using System.Text.Json;
using IpodSync.Core.Crypto;
using IpodSync.Core.Device;
using IpodSync.Core.ItunesDb;
using IpodSync.Core.Sync;

namespace IpodSync.Engine;

/// <summary>
/// One JSON request in, one JSON response out -- the whole surface ipodplayer uses (see Exports.cs for the JNI
/// wrapper). Responses are <c>{"status": http-like code, "body": ...}</c> with the same bodies IpodSync.Web returns.
///   {"op":"selftest"}
///   {"op":"library","root":"/storage/XXXX-XXXX"}
///   {"op":"health","root":..,"firewire":["serial"],"backupRoot":"..","free":0,"total":0}   read-only checks
///   {"op":"backups","backupRoot":".."}                                                    the pre-write backups, newest first
///   {"op":"apply","root":..,"changeSet":{..},"commit":false,"confirm":null,"firewire":["serial"],"backupRoot":".."}
/// "apply" goes through <see cref="RemoteEdits"/> (op allow-list, dry-run confirm token) and <see cref="WritePipeline"/>
/// (backup, write, read-back, re-verify, auto-restore) -- the same code the PC and the ipodsync app run.
/// </summary>
public static class EngineCore
{
    // BouncyCastle only becomes the crypto backend if it passes the known-answer tests; otherwise writes are refused.
    static readonly Lazy<List<string>> CryptoCheck = new(() =>
    {
        var bc = new BouncyCryptoBackend();
        var failures = BouncyCryptoBackend.SelfTest(bc);
        if (failures.Count == 0) CryptoPrimitives.Backend = bc;
        return failures;
    });

    static readonly object WriteLock = new();

    public static string Handle(string requestJson)
    {
        try
        {
            var failures = CryptoCheck.Value;
            using var doc = JsonDocument.Parse(requestJson);
            var r = doc.RootElement;
            string op = r.GetProperty("op").GetString() ?? "";
            string? Str(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            switch (op)
            {
                case "selftest":
                    return Reply(200, new SelfTestReply(failures.Count == 0, failures));
                case "library":
                {
                    string root = Str("root") ?? throw new ArgumentException("root is required");
                    var db = ItunesDbReader.Read(File.ReadAllBytes(IpodDevice.Open(root).ItunesDbPath));
                    return Reply(200, db);
                }
                case "health":
                {
                    string root = Str("root") ?? throw new ArgumentException("root is required");
                    var firewire = r.TryGetProperty("firewire", out var fw0) && fw0.ValueKind == JsonValueKind.Array
                        ? fw0.EnumerateArray().Select(e => e.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList() : [];
                    long Num(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
                    return Reply(200, DeviceHealthCheck.Run(root, SigningInputs.FirewireCandidates(firewire), Str("backupRoot") ?? "", Num("free"), Num("total")));
                }
                case "backups":
                    return Reply(200, DeviceHealthCheck.ListBackups(Str("backupRoot") ?? throw new ArgumentException("backupRoot is required")));
                case "apply":
                {
                    if (failures.Count > 0) return Reply(500, new ErrorReply("crypto self-test failed (" + string.Join(", ", failures) + "); refusing to write"));
                    string root = Str("root") ?? throw new ArgumentException("root is required");
                    string backupRoot = Str("backupRoot") ?? throw new ArgumentException("backupRoot is required");
                    bool commit = r.TryGetProperty("commit", out var c) && c.ValueKind == JsonValueKind.True;
                    string changeSet = r.GetProperty("changeSet").GetRawText();
                    var firewire = r.TryGetProperty("firewire", out var fw) && fw.ValueKind == JsonValueKind.Array
                        ? fw.EnumerateArray().Select(e => e.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList() : [];
                    Directory.CreateDirectory(backupRoot);
                    lock (WriteLock)   // one write at a time, as in the apps
                    {
                        var (status, body) = RemoteEdits.HandleAsync((cs, doCommit, log, _) => Task.FromResult(WritePipeline.Execute(new WritePipeline.Options
                        {
                            Root = root, Changes = cs, Commit = doCommit, Label = "ipodplayer", BackupRoot = backupRoot, Log = log,
                            FirewireCandidates = SigningInputs.FirewireCandidates(firewire),
                            SigningReferences = SigningInputs.References(backupRoot, []),
                        })), null, root, commit, Str("confirm"), changeSet, CancellationToken.None).GetAwaiter().GetResult();
                        return Reply(status, body);
                    }
                }
                default:
                    return Reply(404, new ErrorReply("unknown op " + op));
            }
        }
        catch (Exception ex) { return Reply(500, new ErrorReply(ex.Message)); }
    }

    // source-generated (CoreJson): reflection-based JSON doesn't work under NativeAOT
    static string Reply(int status, object body) =>
        "{\"status\":" + status + ",\"body\":" + JsonSerializer.Serialize(body, body.GetType(), CoreJson.Default) + "}";
}
