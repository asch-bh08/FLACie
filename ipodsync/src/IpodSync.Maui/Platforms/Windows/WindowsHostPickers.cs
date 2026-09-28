using System.Diagnostics;
using IpodSync.Shared.Backend;
using Windows.Storage.Pickers;

namespace IpodSync.Maui.Platforms.Windows;

/// <summary>Native Windows file/folder pickers for the Blazor UI.</summary>
public sealed class WindowsHostPickers : IHostPickers
{
    public bool Available => true;

    private static nint WindowHandle()
    {
        var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as MauiWinUIWindow
            ?? throw new InvalidOperationException("no app window");
        return window.WindowHandle;
    }

    public Task<IReadOnlyList<string>> PickFilesAsync(PickKind kind, bool multiple) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        var picker = new FileOpenPicker { ViewMode = kind == PickKind.Image ? PickerViewMode.Thumbnail : PickerViewMode.List };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHandle());
        string[] types = kind switch
        {
            PickKind.Image => [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".mp3", ".m4a", ".flac"],
            PickKind.Playlist => [".txt", ".m3u", ".m3u8"],
            _ => [.. IpodSync.Core.LocalLibrary.FolderSync.AudioExtensions],
        };
        foreach (var t in types) picker.FileTypeFilter.Add(t);
        picker.SuggestedStartLocation = kind == PickKind.Image ? PickerLocationId.PicturesLibrary : PickerLocationId.MusicLibrary;
        if (multiple)
        {
            var files = await picker.PickMultipleFilesAsync();
            return (IReadOnlyList<string>)(files?.Select(f => f.Path).Where(p => !string.IsNullOrEmpty(p)).ToList() ?? []);
        }
        var file = await picker.PickSingleFileAsync();
        return file is null || string.IsNullOrEmpty(file.Path) ? [] : [file.Path];
    });

    public Task<string?> PickFolderAsync() => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.MusicLibrary };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHandle());
        var folder = await picker.PickSingleFolderAsync();
        return string.IsNullOrEmpty(folder?.Path) ? null : folder.Path;
    });

    public Task OpenFolderAsync(string path)
    {
        if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = false });
        return Task.CompletedTask;
    }
}
