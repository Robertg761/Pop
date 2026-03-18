#!/bin/zsh
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
DOTNET_BIN="$ROOT_DIR/.dotnet/dotnet"

if [[ ! -x "$DOTNET_BIN" ]]; then
  DOTNET_BIN="dotnet"
fi

"$ROOT_DIR/scripts/build-mac-bridge.sh"

PACKAGE_DIR="$ROOT_DIR/src/Pop.App.Mac/NativeHost"
APP_OUTPUT_DIR="$ROOT_DIR/artifacts/mac/app"
APP_BUNDLE="$APP_OUTPUT_DIR/Pop.app"
BIN_PATH="$(swift build --configuration release --package-path "$PACKAGE_DIR" --show-bin-path)"

swift build --configuration release --package-path "$PACKAGE_DIR" --product PopMacApp

rm -rf "$APP_BUNDLE"
mkdir -p "$APP_BUNDLE/Contents/MacOS" "$APP_BUNDLE/Contents/Frameworks" "$APP_BUNDLE/Contents/Resources"

cp "$PACKAGE_DIR/Support/Info.plist" "$APP_BUNDLE/Contents/Info.plist"
cp "$BIN_PATH/PopMacApp" "$APP_BUNDLE/Contents/MacOS/PopMacApp"
cp "$ROOT_DIR/artifacts/mac/bridge/libPopMacBridge.dylib" "$APP_BUNDLE/Contents/Frameworks/libPopMacBridge.dylib"

VERSION="$("$DOTNET_BIN" msbuild -nologo -getProperty:Version "$ROOT_DIR/src/Pop.App.Mac/Pop.App.Mac.csproj" | tail -n 1)"
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $VERSION" "$APP_BUNDLE/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion $VERSION" "$APP_BUNDLE/Contents/Info.plist"
