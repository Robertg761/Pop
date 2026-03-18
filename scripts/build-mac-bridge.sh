#!/bin/zsh
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
DOTNET_BIN="$ROOT_DIR/.dotnet/dotnet"

if [[ ! -x "$DOTNET_BIN" ]]; then
  DOTNET_BIN="dotnet"
fi

OUTPUT_DIR="$ROOT_DIR/artifacts/mac/bridge"
mkdir -p "$OUTPUT_DIR"

"$DOTNET_BIN" publish "$ROOT_DIR/src/Pop.App.Mac/Pop.App.Mac.csproj" \
  --configuration Release \
  --runtime osx-arm64 \
  -p:PublishAot=true \
  -p:NativeLib=Shared \
  --output "$OUTPUT_DIR"

cp "$ROOT_DIR/src/Pop.App.Mac/NativeHost/Sources/CPopMacBridge/include/PopMacBridge.h" "$OUTPUT_DIR/PopMacBridge.h"

if [[ -f "$OUTPUT_DIR/PopMacBridge.dylib" && ! -f "$OUTPUT_DIR/libPopMacBridge.dylib" ]]; then
  cp "$OUTPUT_DIR/PopMacBridge.dylib" "$OUTPUT_DIR/libPopMacBridge.dylib"
fi

if [[ -f "$OUTPUT_DIR/libPopMacBridge.dylib" ]]; then
  install_name_tool -id "@rpath/libPopMacBridge.dylib" "$OUTPUT_DIR/libPopMacBridge.dylib"
fi
