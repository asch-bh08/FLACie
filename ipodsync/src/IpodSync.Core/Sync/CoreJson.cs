using System.Text.Json;
using System.Text.Json.Serialization;
using IpodSync.Core.ItunesDb;

namespace IpodSync.Core.Sync;

/// <summary>Reply bodies of the remote-edit API (IpodSync.Web, the Android app's loopback API, the ipodplayer engine).
/// Concrete records rather than anonymous objects so they can be source-generated for NativeAOT.</summary>
public sealed record ErrorReply(string Error);
public sealed record OpReply(string Op, bool Ok, string Detail);
public sealed record ApplyReply(bool DryRun, bool Ok, int ExitCode, bool Written, bool Restored, string? BackupDir,
    List<OpReply>? Ops, List<string>? Problems, List<string> Log, string? ConfirmToken);
public sealed record SelfTestReply(bool Ok, List<string> Failures);

/// <summary>
/// Compile-time JSON metadata for everything that crosses the engine boundary, so it works under NativeAOT (where
/// reflection-based System.Text.Json can't build converters for these types). Web defaults (camelCase), matching
/// what ASP.NET's minimal APIs return, so every host produces the same JSON.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ItunesDatabase))]
[JsonSerializable(typeof(ErrorReply))]
[JsonSerializable(typeof(ApplyReply))]
[JsonSerializable(typeof(SelfTestReply))]
[JsonSerializable(typeof(HealthReport))]
[JsonSerializable(typeof(List<BackupEntry>))]
public partial class CoreJson : JsonSerializerContext;

/// <summary>The change-set, with the *default* serializer options it has always used: its serialized bytes seed the
/// pipeline's deterministic choices (new persistent ids, file names) and the dry-run confirm token, so they must not
/// change between a dry run and its commit.</summary>
[JsonSerializable(typeof(ChangeSet))]
public partial class ChangeSetJson : JsonSerializerContext;
