using Android.Content;
using Android.Runtime;
using Android.Util;

namespace IpodSync.Maui.Platforms.Android;

/// <summary>
/// Persists unhandled exceptions to a file in the app's private storage and
/// hooks every unhandled-exception surface .NET-on-Android exposes, so a real
/// crash leaves something readable on the next launch (shown as a native
/// dialog by MainActivity) instead of just an OS "app stopped" message with no
/// diagnostic reachable without adb -- which the user's single USB port,
/// currently occupied by the iPod under test, makes impractical to use anyway.
/// </summary>
internal static class CrashLog
{
    private const string FileName = "last_crash.txt";
    private static string PathFor(Context context) => Path.Combine(context.FilesDir!.AbsolutePath, FileName);

    public static void Init(Context context)
    {
        AndroidEnvironment.UnhandledExceptionRaiser += (_, e) =>
        {
            Persist(context, "AndroidEnvironment.UnhandledExceptionRaiser", e.Exception);
            e.Handled = true; // best-effort: keep the app alive when this exception allows it
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Persist(context, "AppDomain.UnhandledException", ex);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Persist(context, "TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }

    private static void Persist(Context context, string source, Exception ex)
    {
        try
        {
            string text = $"{DateTimeOffset.Now:u}  [{source}]\n{ex}\n";
            File.WriteAllText(PathFor(context), text);
            Log.Error("ipodsync", text);
        }
        catch { /* logging must never itself throw */ }
    }

    public static string? TryReadAndClear(Context context)
    {
        string path = PathFor(context);
        if (!File.Exists(path)) return null;
        try
        {
            string text = File.ReadAllText(path);
            File.Delete(path);
            return text;
        }
        catch { return null; }
    }
}
