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
Pop for Linux currently supports X11 desktop sessions.

Run:
  ./Pop

The app watches title-bar drag gestures and exits on Ctrl+C. Settings and
diagnostics are stored under $XDG_CONFIG_HOME/Pop, or ~/.config/Pop when
XDG_CONFIG_HOME is unset.
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
exec "$APPDIR/usr/bin/Pop" "$@"
EOF
chmod +x "$APPDIR/AppRun"

cat > "$APPDIR/pop.desktop" <<'EOF'
[Desktop Entry]
Type=Application
Name=Pop
Comment=Momentum-based window snapping
Exec=Pop
Icon=pop
Terminal=false
Categories=Utility;
EOF
cp "$APPDIR/pop.desktop" "$APPDIR/usr/share/applications/pop.desktop"

if [[ ! -x "$APPIMAGETOOL" ]]; then
  curl --fail --location --retry 3 --retry-delay 2 \
    "https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage" \
    --output "$APPIMAGETOOL"
  chmod +x "$APPIMAGETOOL"
fi

ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 "$APPIMAGETOOL" "$APPDIR" "$APPIMAGE_PATH"

echo "tar_path=$TAR_PATH"
echo "appimage_path=$APPIMAGE_PATH"
