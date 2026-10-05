FLACie 1.0 beta 20 (Android 34, Windows 0.20.0)

- Real audio metadata: every song is read with ffprobe/Jellyfin (no more guessing from the extension). Old Hi-Res files now get the Hi-Res badge.
- Info: spectrum goes to the file's real Nyquist, bigger graphs for Hi-Res, new Playback row (native rate vs resampled).
- Explore: bit depth and sample rate filters.
- Full-screen player menu, playlist "Added / already in" messages with Undo, playlist layout.
- Seeker focus frame and snap-back fixed (web).
- Downloads: fixed the 30 s timeout that logged working YouTube downloads as failures; yt-dlp queue; Soulseek search tweaks; Lidarr now allows Single and EP.

Tested: web in the browser pane (seek hold, focus, menu, toasts with Undo, Explore filters, Info/Playback, probe of 7,100 songs), frank deploy healthy, Android launch + Explore Hi-Res badges on the emulator, Android unit tests, YouTube search/ranking on frank.
Not tested: Android Info panel and snackbar on a device, USB DAC playback, a fresh YouTube download after the fix (needs a new request), Windows MSI install.
