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
APP_EXECUTABLE="$APP_BUNDLE/Contents/MacOS/PopMacApp"
APP_BRIDGE="$APP_BUNDLE/Contents/Frameworks/libPopMacBridge.dylib"
BIN_PATH="$(swift build --configuration release --package-path "$PACKAGE_DIR" --show-bin-path)"
CODE_SIGN_IDENTITY="${POP_MAC_CODESIGN_IDENTITY:-}"

swift build --configuration release --package-path "$PACKAGE_DIR" --product PopMacApp

rm -rf "$APP_BUNDLE"
mkdir -p "$APP_BUNDLE/Contents/MacOS" "$APP_BUNDLE/Contents/Frameworks" "$APP_BUNDLE/Contents/Resources"

cp "$PACKAGE_DIR/Support/Info.plist" "$APP_BUNDLE/Contents/Info.plist"
cp "$BIN_PATH/PopMacApp" "$APP_EXECUTABLE"
cp "$ROOT_DIR/artifacts/mac/bridge/libPopMacBridge.dylib" "$APP_BRIDGE"

VERSION="$("$DOTNET_BIN" msbuild -nologo -getProperty:Version "$ROOT_DIR/src/Pop.App.Mac/Pop.App.Mac.csproj" | tail -n 1)"
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $VERSION" "$APP_BUNDLE/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion $VERSION" "$APP_BUNDLE/Contents/Info.plist"

# TCC permissions like Accessibility are much more reliable when the app has a stable code signature.
# With a real Developer ID identity (POP_MAC_CODESIGN_IDENTITY) we sign with the hardened runtime and
# a secure timestamp so the bundle can be notarized. Without one we fall back to ad-hoc signing.
if [[ -n "$CODE_SIGN_IDENTITY" ]]; then
  SIGN_FLAGS=(--options runtime --timestamp)
else
  CODE_SIGN_IDENTITY="-"
  SIGN_FLAGS=(--timestamp=none)
  cat >&2 <<'EOF'
##############################################################################
# WARNING: POP_MAC_CODESIGN_IDENTITY is not set - falling back to AD-HOC     #
# code signing.                                                              #
#                                                                            #
# Ad-hoc signed builds:                                                      #
#   * will NOT pass Gatekeeper on other Macs (no notarization possible),     #
#   * get a new cdhash on every build, so macOS resets TCC permissions       #
#     (e.g. Accessibility) after every update.                               #
#                                                                            #
# Set POP_MAC_CODESIGN_IDENTITY to a "Developer ID Application" identity to  #
# produce a distributable, notarizable build with a stable signature.        #
##############################################################################
EOF
fi

codesign --force --sign "$CODE_SIGN_IDENTITY" "${SIGN_FLAGS[@]}" "$APP_BRIDGE"
codesign --force --sign "$CODE_SIGN_IDENTITY" "${SIGN_FLAGS[@]}" "$APP_EXECUTABLE"
codesign --force --sign "$CODE_SIGN_IDENTITY" "${SIGN_FLAGS[@]}" "$APP_BUNDLE"
