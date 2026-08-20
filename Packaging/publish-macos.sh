#!/usr/bin/env bash

# Copyright (C) 2026 HyPrism Launcher
# SPDX-License-Identifier: GPL-3.0-only

set -euo pipefail

PACKAGING_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$PACKAGING_DIR/.." && pwd)"
PROJECT_FILE="$PROJECT_ROOT/Sources/HyPrism.Desktop/HyPrism.Desktop.csproj"
APP_ICON="$PROJECT_ROOT/Sources/HyPrism.Desktop/Assets/Images/appicon_512.png"
INFO_PLIST="$PACKAGING_DIR/macos/Info.plist"
OUTPUT_DIR="$PROJECT_ROOT/dist"
TARGETS=()

usage() {
    cat <<'EOF'
Usage: ./Packaging/publish-macos.sh <target> [options]

Targets:
  all   Build the macOS DMG
  dmg   Build the macOS DMG

Options:
  --output <directory>  Artifact directory, defaults to dist
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --output)
            OUTPUT_DIR="${2:?--output requires a directory}"
            shift 2
            ;;
        --help|-h)
            usage
            exit 0
            ;;
        all|dmg)
            TARGETS+=("$1")
            shift
            ;;
        *)
            echo "Unknown macOS publish target: $1" >&2
            exit 2
            ;;
    esac
done

if [[ ${#TARGETS[@]} -eq 0 ]]; then
    TARGETS=(all)
fi

for command in dotnet sips iconutil plutil hdiutil codesign file; do
    command -v "$command" >/dev/null 2>&1 || {
        echo "Required command is unavailable: $command" >&2
        exit 1
    }
done

VERSION="$(dotnet msbuild "$PROJECT_FILE" -nologo -getProperty:Version | tail -n 1 | tr -d '\r')"
if [[ ! "$VERSION" =~ ^[0-9]+(\.[0-9]+){0,2}([-.+][0-9A-Za-z.-]+)?$ ]]; then
    echo "HyPrism.Desktop.csproj contains an invalid Version: $VERSION" >&2
    exit 1
fi
BUNDLE_VERSION="${VERSION%%[-+]*}"

BUILD_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/hyprism-macos-publish.XXXXXX")"
APP_DIR="$BUILD_ROOT/HyPrism.app"
ICONSET_DIR="$BUILD_ROOT/HyPrism.iconset"
trap 'rm -rf "$BUILD_ROOT"' EXIT
mkdir -p "$OUTPUT_DIR" "$APP_DIR/Contents/MacOS" "$APP_DIR/Contents/Resources" "$ICONSET_DIR"

for size in 16 32 64 128 256 512; do
    sips -z "$size" "$size" "$APP_ICON" --out "$ICONSET_DIR/icon_${size}x${size}.png" >/dev/null
    doubled_size=$((size * 2))
    if [[ "$doubled_size" -le 1024 ]]; then
        sips -z "$doubled_size" "$doubled_size" "$APP_ICON" --out "$ICONSET_DIR/icon_${size}x${size}@2x.png" >/dev/null
    fi
done
iconutil -c icns "$ICONSET_DIR" -o "$APP_DIR/Contents/Resources/HyPrism.icns"

dotnet publish "$PROJECT_FILE" \
    --configuration Release \
    --runtime osx-arm64 \
    --self-contained true \
    --output "$APP_DIR/Contents/MacOS"

for host in HyPrism.Desktop HyPrism.LocalNode; do
    test -x "$APP_DIR/Contents/MacOS/$host"
done
file "$APP_DIR/Contents/MacOS/HyPrism.LocalNode" | grep -q 'Mach-O 64-bit executable arm64'

cp "$INFO_PLIST" "$APP_DIR/Contents/Info.plist"
plutil -replace CFBundleShortVersionString -string "$BUNDLE_VERSION" "$APP_DIR/Contents/Info.plist"
plutil -replace CFBundleVersion -string "$BUNDLE_VERSION" "$APP_DIR/Contents/Info.plist"

codesign --force --deep --sign - "$APP_DIR"
codesign --verify --deep --strict "$APP_DIR"

DMG_STAGE="$BUILD_ROOT/dmg"
mkdir -p "$DMG_STAGE"
cp -R "$APP_DIR" "$DMG_STAGE/HyPrism.app"
ln -s /Applications "$DMG_STAGE/Applications"
hdiutil create \
    -volname HyPrism \
    -srcfolder "$DMG_STAGE" \
    -ov \
    -format UDZO \
    "$OUTPUT_DIR/HyPrism-mac-arm64-$VERSION.dmg"

echo "Published macOS artifact to $OUTPUT_DIR"
