#!/usr/bin/env bash
# Pack a platform-specific Claude Desktop MCPB bundle for gramps-web-mcp.
#
# Usage:
#   ./scripts/pack-mcpb.sh <runtime-identifier> [version]
#
# Examples:
#   ./scripts/pack-mcpb.sh osx-arm64
#   ./scripts/pack-mcpb.sh win-x64 1.0.1
#
# Requires: .NET 8 SDK. Optional: npm global @anthropic-ai/mcpb for validate/pack.

set -euo pipefail

RID="${1:?Runtime identifier required: osx-arm64, osx-x64, or win-x64}"
VERSION="${2:-}"

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT="$ROOT/GrampsWeb.Mcp/GrampsWeb.Mcp.csproj"
DIST="$ROOT/dist"
STAGING="$DIST/mcpb-staging-$RID"

if [[ -z "$VERSION" ]]; then
  VERSION="$(grep -o '<Version>[^<]*</Version>' "$PROJECT" | sed 's/<[^>]*>//g' | head -1)"
fi

case "$RID" in
  osx-arm64|osx-x64)
    PLATFORMS='["darwin"]'
  ;;
  win-x64)
    PLATFORMS='["win32"]'
  ;;
  *)
    echo "Unsupported RID: $RID (use osx-arm64, osx-x64, or win-x64)" >&2
    exit 1
  ;;
esac

echo "==> Publishing self-contained binary for $RID (v$VERSION)"
rm -rf "$STAGING"
mkdir -p "$STAGING/server" "$DIST"

dotnet publish "$PROJECT" \
  -c Release \
  -r "$RID" \
  --self-contained true \
  -o "$STAGING/server" \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=none \
  -p:DebugSymbols=false

if [[ "$RID" == win-* ]]; then
  BINARY_NAME="GrampsWeb.Mcp.exe"
else
  BINARY_NAME="GrampsWeb.Mcp"
  chmod +x "$STAGING/server/$BINARY_NAME"
fi

if [[ ! -f "$STAGING/server/$BINARY_NAME" ]]; then
  echo "Expected binary not found: $STAGING/server/$BINARY_NAME" >&2
  exit 1
fi

# Remove non-executable publish debris when single-file publish leaves only the binary
find "$STAGING/server" -mindepth 1 ! -name "$BINARY_NAME" -delete 2>/dev/null || true

cp "$ROOT/mcpb/icon.png" "$STAGING/icon.png"
cp "$ROOT/LICENSE" "$STAGING/LICENSE"

sed \
  -e "s/__VERSION__/$VERSION/g" \
  -e "s/__PLATFORMS__/$PLATFORMS/g" \
  "$ROOT/mcpb/manifest.template.json" > "$STAGING/manifest.json"

OUTPUT_NAME="gramps-web-mcp-claude-desktop-${RID}-v${VERSION}.mcpb"
OUTPUT_PATH="$DIST/$OUTPUT_NAME"

echo "==> Packing $OUTPUT_NAME"
if command -v mcpb >/dev/null 2>&1; then
  mcpb validate "$STAGING/manifest.json"
  mcpb pack "$STAGING" "$OUTPUT_PATH"
else
  echo "mcpb CLI not found; creating zip archive (Claude Desktop accepts .mcpb zip bundles)"
  rm -f "$OUTPUT_PATH"
  (cd "$STAGING" && zip -qr "$OUTPUT_PATH" .)
fi

echo "Created: $OUTPUT_PATH"
