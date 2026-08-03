#!/bin/zsh
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
DOTNET_BIN="$ROOT_DIR/.dotnet/dotnet"

if [[ ! -x "$DOTNET_BIN" ]]; then
  DOTNET_BIN="dotnet"
fi

"$ROOT_DIR/scripts/build-mac-app.sh"

VERSION="$("$DOTNET_BIN" msbuild -nologo -getProperty:Version "$ROOT_DIR/src/Pop.App.Mac/Pop.App.Mac.csproj" | tail -n 1)"
RELEASE_DIR="$ROOT_DIR/artifacts/mac/release"
APP_DIR="$ROOT_DIR/artifacts/mac/app"
APP_BUNDLE="$APP_DIR/Pop.app"
ZIP_PATH="$RELEASE_DIR/Pop-macos-arm64-$VERSION.zip"
DMG_PATH="$RELEASE_DIR/Pop-macos-arm64-$VERSION.dmg"

CODE_SIGN_IDENTITY="${POP_MAC_CODESIGN_IDENTITY:-}"
NOTARY_PROFILE="${POP_MAC_NOTARY_PROFILE:-}"
NOTARY_APPLE_ID="${POP_MAC_NOTARY_APPLE_ID:-}"
NOTARY_TEAM_ID="${POP_MAC_NOTARY_TEAM_ID:-}"
NOTARY_PASSWORD="${POP_MAC_NOTARY_PASSWORD:-}"

NOTARY_ARGS=()
if [[ -n "$NOTARY_PROFILE" ]]; then
  NOTARY_ARGS=(--keychain-profile "$NOTARY_PROFILE")
elif [[ -n "$NOTARY_APPLE_ID" && -n "$NOTARY_TEAM_ID" && -n "$NOTARY_PASSWORD" ]]; then
  NOTARY_ARGS=(--apple-id "$NOTARY_APPLE_ID" --team-id "$NOTARY_TEAM_ID" --password "$NOTARY_PASSWORD")
fi

README_NOTE_PATH="$APP_DIR/READ ME FIRST.txt"
rm -f "$README_NOTE_PATH"

# Unnotarized builds get an install note shipped next to Pop.app in the zip
# and DMG, because Gatekeeper blocks them and TCC permissions reset on update.
if [[ -z "$CODE_SIGN_IDENTITY" || ${#NOTARY_ARGS[@]} -eq 0 ]]; then
  cat > "$README_NOTE_PATH" <<'EOF'
Pop for macOS - READ ME FIRST
=============================

This build of Pop is not notarized by Apple (the project does not currently
have an Apple Developer ID), so macOS blocks it on first launch.

Opening Pop the first time:
  1. Move Pop.app into Applications (or ~/Applications, which also enables
     in-app updates without admin rights).
  2. Double-click Pop.app. macOS will refuse to open it - that is expected.
  3. Open System Settings > Privacy & Security, scroll down to the message
     about "Pop", click "Open Anyway", and confirm.
     (On macOS 13/14 you can instead right-click Pop.app and choose Open.)

  Alternatively, from Terminal:
     xattr -dr com.apple.quarantine /Applications/Pop.app

Accessibility permission:
  Pop needs Accessibility permission to move windows. Because unsigned
  builds change identity with every update, macOS forgets this permission
  each time Pop updates itself. If snapping stops working after an update,
  open System Settings > Privacy & Security > Accessibility, remove the old
  Pop entry (minus button), and add Pop again. Pop will also prompt you at
  launch whenever the permission is missing.

Verifying your download:
  Every release asset has a matching .sha256 file on the GitHub releases
  page. Verify with:  shasum -a 256 -c <asset>.sha256

https://github.com/Robertg761/Pop
EOF
fi

mkdir -p "$RELEASE_DIR"
rm -f "$ZIP_PATH" "$DMG_PATH"
# Zip the app directory contents so the install note (when present) sits next
# to Pop.app at the archive root.
ditto -c -k --sequesterRsrc "$APP_DIR" "$ZIP_PATH"

if [[ -n "$CODE_SIGN_IDENTITY" && ${#NOTARY_ARGS[@]} -gt 0 ]]; then
  echo "Submitting $ZIP_PATH to Apple notary service..."
  xcrun notarytool submit "$ZIP_PATH" --wait "${NOTARY_ARGS[@]}"
  xcrun stapler staple "$APP_BUNDLE"
  # Re-create the zip so the distributed archive contains the stapled ticket.
  rm -f "$ZIP_PATH"
  ditto -c -k --sequesterRsrc "$APP_DIR" "$ZIP_PATH"
elif [[ -n "$CODE_SIGN_IDENTITY" ]]; then
  cat >&2 <<'EOF'
##############################################################################
# WARNING: the app is signed with a Developer ID identity but no notary      #
# credentials were provided (POP_MAC_NOTARY_PROFILE or                       #
# POP_MAC_NOTARY_APPLE_ID/POP_MAC_NOTARY_TEAM_ID/POP_MAC_NOTARY_PASSWORD),   #
# so this build is NOT notarized and will not pass Gatekeeper.               #
##############################################################################
EOF
else
  cat >&2 <<'EOF'
##############################################################################
# WARNING: packaging an AD-HOC signed, UNNOTARIZED build.                    #
# It will not pass Gatekeeper on other Macs, and its cdhash changes every    #
# build so macOS resets TCC permissions (e.g. Accessibility) after each      #
# update. Set POP_MAC_CODESIGN_IDENTITY (Developer ID Application) and       #
# notary credentials to produce a distributable release.                     #
##############################################################################
EOF
fi

hdiutil create -volname "Pop" -srcfolder "$APP_DIR" -ov -format UDZO "$DMG_PATH" >/dev/null

echo "zip_path=$ZIP_PATH"
echo "dmg_path=$DMG_PATH"
