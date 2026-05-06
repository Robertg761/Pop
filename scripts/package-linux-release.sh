#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
DOTNET_BIN="$ROOT_DIR/.dotnet/dotnet"

if [[ ! -x "$DOTNET_BIN" ]]; then
  DOTNET_BIN="dotnet"
fi

VERSION="$("$DOTNET_BIN" msbuild -nologo -getProperty:Version "$ROOT_DIR/src/Pop.App.Linux/Pop.App.Linux.csproj" | tail -n 1)"
PUBLISH_DIR="$ROOT_DIR/artifacts/linux/publish"
STAGE_DIR="$ROOT_DIR/artifacts/linux/package/Pop-linux-x64-$VERSION"
RELEASE_DIR="$ROOT_DIR/artifacts/linux/release"
TAR_PATH="$RELEASE_DIR/Pop-linux-x64-$VERSION.tar.gz"

rm -rf "$PUBLISH_DIR" "$STAGE_DIR"
mkdir -p "$PUBLISH_DIR" "$STAGE_DIR" "$RELEASE_DIR"
rm -f "$TAR_PATH"

"$DOTNET_BIN" publish "$ROOT_DIR/src/Pop.App.Linux/Pop.App.Linux.csproj" \
  --configuration Release \
  --runtime linux-x64 \
  --self-contained true \
  --output "$PUBLISH_DIR"

cp -a "$PUBLISH_DIR"/. "$STAGE_DIR"/
cat > "$STAGE_DIR/README-linux.txt" <<'EOF'
Pop for Linux currently supports X11 desktop sessions.

Run:
  ./Pop

The app watches title-bar drag gestures and exits on Ctrl+C. Settings and
diagnostics are stored under $XDG_CONFIG_HOME/Pop, or ~/.config/Pop when
XDG_CONFIG_HOME is unset.
EOF

tar -C "$(dirname "$STAGE_DIR")" -czf "$TAR_PATH" "$(basename "$STAGE_DIR")"

echo "tar_path=$TAR_PATH"
