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
APPDIR="$ROOT_DIR/artifacts/linux/appimage/Pop.AppDir"
RELEASE_DIR="$ROOT_DIR/artifacts/linux/release"
TOOLS_DIR="$ROOT_DIR/artifacts/linux/tools"
TAR_PATH="$RELEASE_DIR/Pop-linux-x64-$VERSION.tar.gz"
APPIMAGE_PATH="$RELEASE_DIR/Pop-linux-x64-$VERSION.AppImage"
APPIMAGETOOL="$TOOLS_DIR/appimagetool-x86_64.AppImage"

rm -rf "$PUBLISH_DIR" "$STAGE_DIR" "$APPDIR"
mkdir -p "$PUBLISH_DIR" "$STAGE_DIR" "$APPDIR" "$RELEASE_DIR" "$TOOLS_DIR"
rm -f "$RELEASE_DIR"/Pop-linux-x64-*.tar.gz "$RELEASE_DIR"/Pop-linux-x64-*.AppImage

"$DOTNET_BIN" publish "$ROOT_DIR/src/Pop.App.Linux/Pop.App.Linux.csproj" \
  --configuration Release \
  --runtime linux-x64 \
  --self-contained true \
  --output "$PUBLISH_DIR"

cp -a "$PUBLISH_DIR"/. "$STAGE_DIR"/
cat > "$STAGE_DIR/README-linux.txt" <<'EOF'
Pop for Linux currently supports X11 desktop sessions and Plasma Wayland
through a KWin script.

Run:
  ./Pop

The app watches title-bar drag gestures and exits on Ctrl+C. The current Linux
build is terminal-first and does not have a tray icon or settings window yet.
Settings, diagnostics, and AppImage launch logs are stored under
$XDG_CONFIG_HOME/Pop, or ~/.config/Pop when XDG_CONFIG_HOME is unset.
EOF

tar -C "$(dirname "$STAGE_DIR")" -czf "$TAR_PATH" "$(basename "$STAGE_DIR")"

mkdir -p "$APPDIR/usr/bin" "$APPDIR/usr/share/applications" "$APPDIR/usr/share/icons/hicolor/256x256/apps"
cp -a "$PUBLISH_DIR"/. "$APPDIR/usr/bin"/
cp "$ROOT_DIR/official_icon.png" "$APPDIR/pop.png"
cp "$ROOT_DIR/official_icon.png" "$APPDIR/usr/share/icons/hicolor/256x256/apps/pop.png"
cat > "$APPDIR/AppRun" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail

APPDIR="$(cd "$(dirname "$0")" && pwd)"
POP_BIN="$APPDIR/usr/bin/Pop"

notify() {
  if command -v notify-send >/dev/null 2>&1; then
    notify-send "Pop" "$1" || true
  fi
}

config_home="${XDG_CONFIG_HOME:-$HOME/.config}"
log_dir="$config_home/Pop"
log_path="$log_dir/launch.log"

if [[ -t 1 || -t 2 ]]; then
  exec "$POP_BIN" "$@"
fi

mkdir -p "$log_dir"
echo "[$(date --iso-8601=seconds)] Starting Pop from AppImage." >> "$log_path"
notify "Pop is running. Launch output is logged to $log_path."

"$POP_BIN" "$@" >> "$log_path" 2>&1
exit_code=$?
if [[ $exit_code -eq 0 ]]; then
  exit 0
fi

notify "Pop could not start. See $log_path for details."
exit "$exit_code"
EOF
chmod +x "$APPDIR/AppRun"

cat > "$APPDIR/pop.desktop" <<'EOF'
[Desktop Entry]
Type=Application
Name=Pop
Comment=Momentum-based window snapping
Exec=Pop
Icon=pop
Terminal=true
Categories=Utility;
EOF
cp "$APPDIR/pop.desktop" "$APPDIR/usr/share/applications/pop.desktop"

if [[ ! -x "$APPIMAGETOOL" ]]; then
  # Pin to an immutable release tag rather than the moving 'continuous' tag so the tool that runs
  # inside this write-token job cannot be swapped underneath us. Set APPIMAGETOOL_SHA256 to the
  # expected checksum to enforce integrity (recommended); if unset the build only warns.
  APPIMAGETOOL_TAG="${APPIMAGETOOL_TAG:-13}"
  curl --fail --location --retry 3 --retry-delay 2 \
    "https://github.com/AppImage/AppImageKit/releases/download/${APPIMAGETOOL_TAG}/appimagetool-x86_64.AppImage" \
    --output "$APPIMAGETOOL"

  if [[ -n "${APPIMAGETOOL_SHA256:-}" ]]; then
    echo "${APPIMAGETOOL_SHA256}  ${APPIMAGETOOL}" | sha256sum --check --status \
      || { echo "appimagetool checksum verification failed." >&2; exit 1; }
  else
    echo "WARNING: APPIMAGETOOL_SHA256 is not set; skipping appimagetool integrity check." >&2
    echo "Downloaded appimagetool sha256: $(sha256sum "$APPIMAGETOOL" | cut -d' ' -f1)" >&2
  fi

  chmod +x "$APPIMAGETOOL"
fi

ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 "$APPIMAGETOOL" "$APPDIR" "$APPIMAGE_PATH"

echo "tar_path=$TAR_PATH"
echo "appimage_path=$APPIMAGE_PATH"
