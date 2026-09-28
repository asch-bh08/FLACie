namespace IpodSync.Maui;

public partial class MainPage : ContentPage
{
	public MainPage()
	{
		InitializeComponent();
	}

	/// <summary>
	/// Windows only: hand the WebView to WindowsMediaSource so it can map the iPod (and the
	/// playback conversion cache) to virtual host names — a page can't load file:// URLs, so
	/// that is how the &lt;audio&gt; element reaches the iPod's own files.
	/// </summary>
	private void OnWebViewInitialized(object? sender, EventArgs e)
	{
#if WINDOWS
		if (e is Microsoft.AspNetCore.Components.WebView.BlazorWebViewInitializedEventArgs args)
			Platforms.Windows.WindowsMediaSource.Attach(args.WebView.CoreWebView2);
#endif
	}
}
