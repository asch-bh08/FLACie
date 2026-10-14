Beta 65 (Android 80, MSI 0.65.0, FLACie Web 1.0-beta65). Still a beta, not a 1.0 final.

**Fixed (web and Windows): the iPod mode's cover art was missing everywhere.** The covers asked for a picture called "ArtKey" instead of the song's own, so every cover stayed the grey note. They now show in the lists, the preview pane, Now Playing and Cover Flow, and a test fails if any cover asks for something that is not a real picture.

**Changed (web and Windows): the iPod mode is easier to use and fits the window.**
- **It always fits.** The device is scaled to the free space beside the panel and re-fitted whenever the window, the model or the look changes. Before, changing "Wheel only" and back to "Device" lost the fit and the iPod shrank to a small fixed size. A test now checks that both looks fill the height and that going back gives exactly the first size.
- **A panel beside the device** (it opens by default on a wide window, behind the sliders button on a narrow one): what is playing with its **big cover**, previous / play-pause / next, the model picker by group (Classic, mini, nano, touch) with year, the colours as swatches, Look (Device / Wheel only), and a short "how to use". Picking something in the panel starts the iPod again at its main menu.
- **Easier controls**: a click anywhere on the iPod area gives it the keys; tooltips on the ring ("Menu (Esc)", "Next (right arrow)", ...); the mouse wheel and trackpads scroll one row per notch (they used to fly through the list); the panel's buttons never take the keys or act twice.
- The Now Playing screens of the nano and classic models were drawn too low (the time was cut off); fixed.

**New (Android): Cover Flow on the nano and classic 6/7 screens too** (it was only in the Classic menus): "Cover Flow" in their main menu, the same row of covers in perspective.

**Tested** (this build)
- Android unit tests: 414 run, 2 skipped (as before), 0 failed. .NET tests: 329 pass. Browser tests: 153 pass, 0 fail (one full run, nothing else running); the iPod spec now has 10 tests: the 8 of beta 64 plus one for the covers (every cover asks for a real picture and gets one: the lists, the pane, Now Playing, Cover Flow and the panel) and one for the fit (both looks fill the height, going back gives the first size, the panel is open on a wide window and closed on a narrow one, the keys survive a click on the panel).
- Release APK versionCode 80 signed with the owner key (SHA-256 6a4eb335...97a106); installed on a clean emulator and run against the 10,000-song mock: first sync 10,003 songs (first songs after 13 s), scrolling 103 swipes with 611 requests, a skip-burst of 40 in 2.6 s with the next song ready after 5 s and 66 requests, warm start 10.4 s, kill and restore: queue restored, 0 crash lines.
- On the emulator: the nano 3rd gen main menu shows Cover Flow and opens it without a crash (the emulator had no library, so no covers were shown there; the same drawing code showed covers on the Classic in beta 63).
- The MSI (0.65.0) was built, not installed. FLACie Web was deployed.

**Not tested**
- Finger feel on a touch screen, the clicker sound and haptics, and the look on the owner's own screen: only a browser at 1366x768 and phone-size windows were used. Windows was not run in the installed app.
- **Not built:** Chromecast (a decision for the owner, docs/spec/chromecast.md); a title cross-fade and a bitmap-font LCD on Android; Cover Flow on the mono web screens (the real mono iPods had none).
