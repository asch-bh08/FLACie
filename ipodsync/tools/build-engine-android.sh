#!/usr/bin/env bash
# Builds the ipodsync engine as an Android native library for ipodplayer: libipodsync.so (NativeAOT) plus the
# libe_sqlite3.so it loads, for arm64-v8a.
#
#   ipodsync/tools/build-engine-android.sh [out-dir]   (run on Linux or WSL -- NativeAOT can't cross-compile from Windows)
#
# out-dir defaults to the FLACie Android app's jniLibs (../android/app/src/main/jniLibs) in this monorepo.
#
# Needs: .NET 9 SDK (DOTNET, default ~/.dotnet/dotnet) and the Android NDK r27+ (NDK, default ~/ipodtc/android-ndk-r27c).
# The sources are copied to a Linux-side work dir first (building on /mnt/c is very slow and trips over CRLF).
set -euo pipefail
REPO="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${1:-$REPO/../android/app/src/main/jniLibs}"
NDK="${NDK:-$HOME/ipodtc/android-ndk-r27c}"
DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
TC="$NDK/toolchains/llvm/prebuilt/linux-x86_64"
WORK="${WORK:-$HOME/ipodsync-engine-build}"
[ -x "$DOTNET" ] || { echo "no .NET SDK at $DOTNET (curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 9.0)"; exit 2; }
[ -d "$TC" ] || { echo "no Android NDK at $NDK"; exit 2; }
export PATH="$TC/bin:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1

rm -rf "$WORK" && mkdir -p "$WORK"
cp -r "$REPO/src/IpodSync.Core" "$REPO/src/IpodSync.Engine" "$WORK/"
find "$WORK" -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +

cd "$WORK/IpodSync.Engine"
"$DOTNET" publish -c Release -r linux-bionic-arm64 \
  -p:DisableUnsupportedError=true -p:PublishAotUsingRuntimePack=true \
  -p:CppCompilerAndLinker=aarch64-linux-android30-clang -p:ObjCopyName=llvm-objcopy \
  -p:SysRoot="$TC/sysroot" -o "$WORK/out"

mkdir -p "$OUT/arm64-v8a"
cp "$WORK/out/ipodsync.so" "$OUT/arm64-v8a/libipodsync.so"
# Android's e_sqlite3 build ships inside the SQLitePCLRaw Android package's AAR
AAR="${SQLITE_AAR:-$(find "$HOME/.nuget/packages/sqlitepclraw.lib.e_sqlite3.android" /mnt/c/Users/*/.nuget/packages/sqlitepclraw.lib.e_sqlite3.android -name '*.aar' 2>/dev/null | sort | tail -1)}"
[ -n "$AAR" ] || { echo "restore SQLitePCLRaw.lib.e_sqlite3.android first (any MAUI Android build does)"; exit 2; }
python3 -c 'import zipfile,sys; open(sys.argv[2],"wb").write(zipfile.ZipFile(sys.argv[1]).read("jni/arm64-v8a/libe_sqlite3.so"))' "$AAR" "$OUT/arm64-v8a/libe_sqlite3.so"
"$TC/bin/llvm-strip" --strip-unneeded "$OUT/arm64-v8a/libipodsync.so"
ls -la "$OUT/arm64-v8a"
