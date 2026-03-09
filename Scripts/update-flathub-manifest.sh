#!/usr/bin/env bash

# This script fetches the latest Linux x64 tarball from the
# HyPrism GitHub releases, computes its sha256 checksum and
# updates the Flathub manifest at
# Properties/linux/flathub/io.github.HyPrismTeam.HyPrism.yml
# with the concrete download URL and corresponding sha256.
#
# Dependencies: curl, jq, sha256sum (coreutils), sed

set -euo pipefail

MANIFEST="$(dirname "$0")/../Properties/linux/flathub/io.github.HyPrismTeam.HyPrism.yml"

# query GitHub API for the latest release
API="https://api.github.com/repos/hyprismteam/HyPrism/releases/latest"

# find the first asset matching our pattern
read -r url name < <(curl -s "$API" \
    | jq -r '.assets[] |
        select(.name | test("HyPrism-linux-x64-.*\\.tar\\.xz$")) |
        "\(.browser_download_url) \(.name)"' | head -n1)

if [[ -z "$url" || -z "$name" ]]; then
    echo "error: unable to locate HyPrism-linux-x64 archive in latest release" >&2
    exit 1
fi

echo "Latest asset: $name"

# download the asset to a temporary directory
tmpdir=$(mktemp -d)
trap 'rm -rf "$tmpdir"' EXIT

curl -L -o "$tmpdir/$name" "$url"

# compute sha256
sha=$(sha256sum "$tmpdir/$name" | cut -d' ' -f1)

echo "computed sha256: $sha"

# regenerate entire manifest from template, injecting URL and checksum
cat >"$MANIFEST" <<EOF
app-id: io.github.HyPrismTeam.HyPrism
runtime: org.freedesktop.Platform
runtime-version: '25.08'
sdk: org.freedesktop.Sdk
base: org.electronjs.Electron2.BaseApp
base-version: '25.08'
command: hyprism-launcher-wrapper

sdk-extensions:
  - org.freedesktop.Sdk.Extension.node22

add-extensions:
  org.freedesktop.Platform.codecs_extra.i386:
    directory: lib/i386-linux-gnu/codecs-extra
    version: '25.08-extra'
    autodelete: false
    add-ld-path: lib
      
build-options:
  append-path: /usr/lib/sdk/node22/bin

finish-args:
  - --share=ipc
  - --socket=fallback-x11
  - --socket=pulseaudio
  - --share=network
  - --device=dri
  - --talk-name=org.freedesktop.Notifications
  - --env=LD_LIBRARY_PATH=/app/lib
  - --env=ELECTRON_TRASH=gio
  - --env=XCURSOR_PATH=/run/host/user-share/icons:/run/host/share/icons

modules:

  - name: HyPrism
    buildsystem: simple
    build-commands:
      - mkdir -p /app/lib/i386-linux-gnu/codecs-extra
      - install -d /app/HyPrism
      - cp -R linux-unpacked/* /app/HyPrism
      - chmod +x /app/HyPrism/chrome-sandbox /app/HyPrism/HyPrism
      - cp -R wwwroot /app/wwwroot
      - install -Dm755 hyprism-launcher-wrapper.sh /app/bin/hyprism-launcher-wrapper
      - install -Dm644 io.github.HyPrismTeam.HyPrism.png /app/share/icons/hicolor/256x256/apps/io.github.HyPrismTeam.HyPrism.png
      - install -Dm644 io.github.HyPrismTeam.HyPrism.desktop /app/share/applications/io.github.HyPrismTeam.HyPrism.desktop
      - install -Dm644 io.github.HyPrismTeam.HyPrism.metainfo.xml /app/share/metainfo/io.github.HyPrismTeam.HyPrism.metainfo.xml
      - ln -s /app/HyPrism/HyPrism /app/bin/HyPrism
    sources:
      - type: git
        url:  https://github.com/HyPrismTeam/HyPrism
        branch: main
      - type: archive
        url: $url
        sha256: $sha
      - type: dir
        path: ../../../bin/Release/net10.0/linux-x64/publish/linux-unpacked
        dest: linux-unpacked
      - type: dir
        path: wwwroot
      - type: file
        path: hyprism-launcher-wrapper.sh
      - type: file
        path: io.github.HyPrismTeam.HyPrism.png
      - type: file
        path: io.github.HyPrismTeam.HyPrism.desktop
      - type: file
        path: io.github.HyPrismTeam.HyPrism.metainfo.xml
EOF

echo "Manifest regenerated and updated: $MANIFEST"