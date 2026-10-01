using System.Diagnostics;
using System.Net.Http;
using System.Net.NetworkInformation;

namespace IpodSync.Maui.Platforms.Windows;

/// <summary>
/// The FLACie music player inside the desktop app: the same FLACie Web server the Docker image runs, started as a child process on a
/// loopback port and shown in a WebView, so the player looks and behaves exactly like the web and the phone. It lives in the
/// <c>flacie-web</c> folder next to the app (tools\build-apps.ps1 publishes it there) and only listens on 127.0.0.1. The server stops when
/// this app does (it watches this process id), so no orphan is left behind.
/// </summary>
public static class FlacieHost
{
	static Process? process;
	static Uri? url;
	static readonly SemaphoreSlim gate = new(1, 1);

	static string ExePath => Path.Combine(AppContext.BaseDirectory, "flacie-web", "FLACie.Server.exe");

	/// <summary>False in a build that wasn't packaged with the player (then the app is just the iPod manager).</summary>
	public static bool Available => File.Exists(ExePath);

	public static async Task<Uri> StartAsync(CancellationToken ct = default)
	{
		await gate.WaitAsync(ct);
		try
		{
			if (url is not null && process is { HasExited: false }) return url;
			var port = FreePort(5287);
			var psi = new ProcessStartInfo(ExePath)
			{
				UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(ExePath)!,
			};
			psi.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
			psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
			psi.Environment["FLACIE_DATA"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FLACie", "web");
			psi.Environment["FLACIE_PARENT_PID"] = Environment.ProcessId.ToString();
			process = Process.Start(psi) ?? throw new InvalidOperationException("Couldn't start the FLACie player.");
			AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop();

			using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
			var deadline = DateTime.UtcNow.AddSeconds(40);
			while (DateTime.UtcNow < deadline)
			{
				if (process.HasExited) throw new InvalidOperationException("The FLACie player stopped while starting.");
				try { if ((await http.GetAsync($"http://127.0.0.1:{port}/healthz", ct)).IsSuccessStatusCode) return url = new Uri($"http://127.0.0.1:{port}/"); } catch (HttpRequestException) { } catch (TaskCanceledException) { }
				await Task.Delay(300, ct);
			}
			throw new TimeoutException("The FLACie player didn't start in time.");
		}
		finally { gate.Release(); }
	}

	public static void Stop()
	{
		try { if (process is { HasExited: false }) process.Kill(true); } catch (Exception) { }
		process = null; url = null;
	}

	// a fixed first choice keeps the sign-in cookie (it belongs to the address, port included) valid between runs
	static int FreePort(int preferred)
	{
		var used = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Select(e => e.Port).ToHashSet();
		for (var p = preferred; p < preferred + 40; p++) if (!used.Contains(p)) return p;
		return preferred;
	}
}
