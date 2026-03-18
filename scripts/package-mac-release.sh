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
APP_BUNDLE="$ROOT_DIR/artifacts/mac/app/Pop.app"
ZIP_PATH="$RELEASE_DIR/Pop-macos-arm64-$VERSION.zip"

mkdir -p "$RELEASE_DIR"
rm -f "$ZIP_PATH"
ditto -c -k --sequesterRsrc --keepParent "$APP_BUNDLE" "$ZIP_PATH"

echo "$ZIP_PATH"
