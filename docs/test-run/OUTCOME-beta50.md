# Beta 50 outcome (2026-10-11 evening)

- Released: v1.0.0-beta50 (APK 45 MB signed with the beta 48/49 key, MSI 0.50.0), from main at 1fd8a76. Release notes: docs/release-notes-beta50.md (Tested / Not tested lists).
- Deployed: flacie-web only on frank, rollback image `flacie-web:rollback-2026-10-11-pre50` (the beta 49 + earlier image), /healthz "ok" locally and on the public address, FLACIE_ALLOW_NAS_LOGIN=false, flacie-ytdl untouched.
- NOT confirmed: that the owner can sign in as admin on the new server (needs Quick Connect; do this first and roll back with `docker tag flacie-web:rollback-2026-10-11-pre50 flacie-web:local` + `docker compose up -d --force-recreate flacie` if it fails).
- Open: Fold crash not re-tested on the real device (emulator loops clean); first-NAS-scan jank not measured on a fresh install; first-launch screens never seen on a device; background MEDIA_PLAY after audio-focus loss; queue reorder is a feature not in this build; desktop device label "Edge on Windows".
- Test reports: G-rg.md, G-win.md, G-emu.md, G1-web.md, G2-web.md, G2-android.md. Rollback and deploy recipe: docs/private/HANDOFF-2026-10-11.md.