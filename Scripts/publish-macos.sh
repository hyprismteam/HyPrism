#!/usr/bin/env bash

# Copyright (C) 2026 HyPrism Launcher
# SPDX-License-Identifier: GPL-3.0-only

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PROJECT_FILE="$PROJECT_ROOT/Sources/HyPrism.Desktop/HyPrism.Desktop.csproj"
APP_ICON="$PROJECT_ROOT/Sources/HyPrism.Desktop/Assets/Images/appicon_512.png"
VERSION=""
OUTPUT_DIR="$PROJECT_ROOT/dist"
TARGETS=()

usage() {
    cat <<'EOF'
Usage: ./Scripts/publish.sh <target> [options]

Targets:
  all   Build the macOS DMG
  dmg   Build the macOS DMG

Options:
  --version <version>   Version embedded in the DMG name
  --output <directory>  Artifact directory, defaults to dist
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --version)
            VERSION="${2:?--version requires a value}"
            shift 2
            ;;
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

if [[ -z "$VERSION" ]]; then
    if [[ "${GITHUB_REF_NAME:-}" == v* ]]; then
        VERSION="${GITHUB_REF_NAME#v}"
    elif [[ -n "${GITHUB_RUN_NUMBER:-}" ]]; then
        VERSION="ci-${GITHUB_RUN_NUMBER}"
    else
        VERSION="local"
    fi
fi

if [[ "$VERSION" =~ ^[0-9]+(\.[0-9]+){0,2}$ ]]; then
    BUNDLE_VERSION="$VERSION"
else
    BUNDLE_VERSION="0.0.0"
fi

for command in dotnet sips iconutil plutil hdiutil codesign file; do
    command -v "$command" >/dev/null 2>&1 || {
        echo "Required command is unavailable: $command" >&2
        exit 1
    }
done

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

plutil -create xml1 "$APP_DIR/Contents/Info.plist"
plutil -insert CFBundleDevelopmentRegion -string en "$APP_DIR/Contents/Info.plist"
plutil -insert CFBundleDisplayName -string HyPrism "$APP_DIR/Contents/Info.plist"
plutil -insert CFBundleExecutable -string HyPrism.Desktop "$APP_DIR/Contents/Info.plist"
plutil -insert CFBundleIconFile -string HyPrism.icns "$APP_DIR/Contents/Info.plist"
plutil -insert CFBundleIdentifier -string io.github.hyprismteam.HyPrism "$APP_DIR/Contents/Info.plist"
plutil -insert CFBundleInfoDictionaryVersion -string 6.0 "$APP_DIR/Contents/Info.plist"
plutil -insert CFBundleName -string HyPrism "$APP_DIR/Contents/Info.plist"
plutil -insert CFBundlePackageType -string APPL "$APP_DIR/Contents/Info.plist"
plutil -insert CFBundleShortVersionString -string "$BUNDLE_VERSION" "$APP_DIR/Contents/Info.plist"
plutil -insert CFBundleVersion -string "${GITHUB_RUN_NUMBER:-1}" "$APP_DIR/Contents/Info.plist"
plutil -insert LSMinimumSystemVersion -string 12.0 "$APP_DIR/Contents/Info.plist"
plutil -insert NSHighResolutionCapable -bool true "$APP_DIR/Contents/Info.plist"

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
