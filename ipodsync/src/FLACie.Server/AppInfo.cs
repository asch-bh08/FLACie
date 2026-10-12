using System.Reflection;

namespace FLACie.Server;

/// <summary>What this build is: shown in Settings > About and at /licenses.</summary>
public static class AppInfo
{
    public const string FallbackVersion = "1.0-beta52";
    public static string Version { get; } = Environment.GetEnvironmentVariable("FLACIE_VERSION") is { Length: > 0 } v ? v
        : typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] is { Length: > 0 } iv ? iv : FallbackVersion;

    /// <summary>The licence notices that ship with the app (THIRD-PARTY-NOTICES.md next to the program), or a pointer when this build has none.</summary>
    public static string Licenses()
    {
        try
        {
            var f = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md");
            if (File.Exists(f)) return File.ReadAllText(f);
        }
        catch (Exception) { }
        return "FLACie " + Version + "\n\nThe third-party licence notices (THIRD-PARTY-NOTICES.md) are not bundled with this build. They are in the FLACie source repository.\n";
    }
}