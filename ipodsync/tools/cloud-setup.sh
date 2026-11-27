#!/usr/bin/env bash
# Sets up a claude.ai/code cloud session to build and test FLACie (no emulator there): .NET 9 SDK and the Android SDK.
# Needs the environment's network to allow builds.dotnet.microsoft.com and dl.google.com (package registries are allowed by default).
# Run from the repository root: bash ipodsync/tools/cloud-setup.sh
set -euo pipefail
# .NET 9 (the Ubuntu feed has only 8 and 10)
if [ ! -x "$HOME/.dotnet/dotnet" ]; then
  curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --channel 9.0 --install-dir "$HOME/.dotnet"
fi
# Android SDK: command-line tools, then the platform and build tools the app compiles against (android/app/build.gradle.kts: compileSdk 34)
SDK="$HOME/android-sdk"
if [ ! -d "$SDK/platforms/android-34" ]; then
  mkdir -p "$SDK/cmdline-tools"
  curl -sSL -o /tmp/ct.zip https://dl.google.com/android/repository/commandlinetools-linux-11076708_latest.zip
  unzip -qo /tmp/ct.zip -d "$SDK/cmdline-tools" && rm -rf "$SDK/cmdline-tools/latest" && mv "$SDK/cmdline-tools/cmdline-tools" "$SDK/cmdline-tools/latest"
  yes | "$SDK/cmdline-tools/latest/bin/sdkmanager" --sdk_root="$SDK" "platforms;android-34" "build-tools;34.0.0" "platform-tools" >/dev/null
fi
echo "sdk.dir=$SDK" > android/local.properties   # git-ignored
chmod +x android/gradlew
# Playwright specs: the browser is already on the machine
(cd ipodsync/tests/web-smoke && npm ci --silent)
cat <<'MSG'
Done. In each shell:
  export PATH=$HOME/.dotnet:$PATH PW_CHROMIUM=/opt/pw-browsers/chromium
  dotnet test ipodsync/tests/FLACie.Tests
  (cd android && ./gradlew testDebugUnitTest assembleDebug)
  (cd ipodsync/tests/web-smoke && npx playwright test <spec>)
  PARITY_LOCKED=1 node ipodsync/tools/parity/run.mjs --web --screens <ids>   (skips the Windows lock; kill a stale mock on 8311 first)
MSG
