namespace FLACie.Hybrid;

public sealed class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState) => new(new MainPage()) { Title = "FLACie" };
}

/// <summary>The whole app: one BlazorWebView showing the sign-in, then the Retro page.</summary>
public sealed class MainPage : ContentPage
{
    public MainPage()
    {
        BackgroundColor = Color.FromArgb("#0b0b0e");
        var view = new Microsoft.AspNetCore.Components.WebView.Maui.BlazorWebView { HostPage = "wwwroot/index.html" };
        view.RootComponents.Add(new Microsoft.AspNetCore.Components.WebView.Maui.RootComponent { Selector = "#app", ComponentType = typeof(Components.Routes) });
#if ANDROID
        view.BlazorWebViewInitialized += (_, e) => Platforms.Android.WebViewSetup.Apply(e.WebView, Handler!.MauiContext!.Services);
#endif
        Content = view;
    }
}
