using System.Reflection;

namespace FLACie.Server;

/// <summary>What this build is: shown in Settings > About, at /licenses and /privacy.</summary>
public static class AppInfo
{
    public const string FallbackVersion = "1.0 b133";
    public static string Version { get; } = Environment.GetEnvironmentVariable("FLACIE_VERSION") is { Length: > 0 } v ? v
        : typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] is { Length: > 0 } iv ? iv : FallbackVersion;

    // the legal files that ship with the app (legal/ next to the program: copies of the ones at the top of the repository, see FLACie.Server.csproj)
    static string? Legal(string name)
    {
        try { var f = Path.Combine(AppContext.BaseDirectory, "legal", name); return File.Exists(f) ? File.ReadAllText(f) : null; }
        catch (Exception) { return null; }
    }

    /// <summary>What /licenses shows: the third-party notices that ship with the app, then FLACie's own MIT licence (with its "as is" clause).</summary>
    public static string Licenses() =>
        (Legal("THIRD-PARTY-NOTICES.md") ?? "The third-party licence notices (THIRD-PARTY-NOTICES.md) are not bundled with this build. They are in the FLACie source repository.\n")
        + "\n\n---------------------------------------------------------------------------\nFLACie " + Version + " itself\n\n"
        + (Legal("LICENSE.txt") ?? "MIT License. FLACie is provided \"as is\", without warranty of any kind.\n");

    /// <summary>The full licence texts the notices link to (licenses/LGPL-3.0.txt and so on), by file name only: any other name is null, so nothing else can be read.</summary>
    public static readonly string[] LicenseTexts = ["Apache-2.0.txt", "GPL-3.0.txt", "LGPL-2.1.txt", "LGPL-3.0.txt"];
    public static string? LicenseText(string name) => LicenseTexts.Contains(name) ? Legal(Path.Combine("licenses", name)) : null;

    /// <summary>The privacy notice (PRIVACY.md): what a server keeps and what it sends where. Public, so someone can read it before signing in.</summary>
    public static string Privacy() => Legal("PRIVACY.md") ?? "The privacy notice (PRIVACY.md) is not bundled with this build. It is in the FLACie source repository.\n";
}
