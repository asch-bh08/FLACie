Beta 44 (Android 59, MSI 0.44.0). A quick fix on top of beta 43. Still a beta, not a 1.0 final.

**Fixed: the Devices popup was hidden behind the full-screen player (web, Windows)**
- With the full-screen player open, pressing Devices in the bottom bar opened "Connect to a device" underneath the player, so it looked broken. This was my own regression: in beta 41 I raised the full-screen player above Explore's filter bar, which also put it above the popups that open upwards from the bottom bar. The bottom bar now sits above the player (the player stops where the bar starts, so nothing else moves). The Explore filter bar is still under the player.

**Tested**
- Web in the browser: with the full-screen player open on Explore, the Devices popup opens and is on top (three points across it all hit the popup, and it lists "This browser" and the other player with Play here / Send here). Frank runs this web code and the public address serves the new stylesheet.
- Windows: installer build and app smoke test (starts, /healthz ok, the new stylesheet is inside it, closing the app stops the server). Android: unit tests and release build; no Android code changed.

**Not tested**
- The Devices popup on phone widths (there the full-screen player covers the bottom bar entirely, as before); the rest of the beta 43 not-tested list still applies (see the beta 43 notes): the Android app signed in against server data, Jams, notifications, a real phone or Fold, and installing the MSI itself.
