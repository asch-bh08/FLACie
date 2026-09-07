using Android.App;
using Android.Content;
using Microsoft.Maui.ApplicationModel;

namespace IpodSync.Maui.Platforms.Android;

/// <summary>
/// Bridges Android's Storage Access Framework document-tree picker (a plain
/// Activity-result flow) into an awaitable call. MainActivity forwards its
/// OnActivityResult here; nothing else needs to know an Activity is involved.
/// </summary>
public static class SafBridge
{
    private const int RequestCode = 4242;
    private static TaskCompletionSource<global::Android.Net.Uri?>? _pending;

    /// <summary>Shows the system folder picker and waits for the user's choice.
    /// Returns null if they backed out without picking anything.</summary>
    public static Task<global::Android.Net.Uri?> PickTreeAsync()
    {
        _pending = new TaskCompletionSource<global::Android.Net.Uri?>();
        var intent = new Intent(Intent.ActionOpenDocumentTree);
        Platform.CurrentActivity?.StartActivityForResult(intent, RequestCode);
        return _pending.Task;
    }

    public static void HandleActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode != RequestCode || _pending is null) return;
        var uri = resultCode == Result.Ok ? data?.Data : null;
        _pending.TrySetResult(uri);
        _pending = null;
    }
}
