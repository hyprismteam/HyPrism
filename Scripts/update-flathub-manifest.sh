#!/usr/bin/env bash

# This script fetches the latest Linux x64 tarball from the
# HyPrism GitHub releases, computes its sha256 checksum and
# updates the Flathub manifest at
# Properties/linux/flathub/io.github.hyprismteam.HyPrism.module.yml
# with the concrete download URL and corresponding sha256.
#
# Dependencies: curl, jq, sha256sum (coreutils), sed

set -euo pipefail

MANIFEST="$(dirname "$0")/../Properties/linux/flathub/io.github.hyprismteam.HyPrism.yml"

# query GitHub API for the latest release
API="https://api.github.com/repos/hyprismteam/HyPrism/releases/latest"

# also fetch the SHA of the tip of the main branch
# we can use the GitHub API which doesn't require a local repo
MAIN_SHA=$(curl -s "https://api.github.com/repos/hyprismteam/HyPrism/branches/main" \
    | jq -r .commit.sha)
if [[ -z "$MAIN_SHA" || "$MAIN_SHA" == "null" ]]; then
    echo "error: unable to fetch main branch SHA" >&2
    exit 1
fi


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

# build final manifest by combining header and module fragments
# header comes from flatpak yaml, modules from flathub module file
FLATPAK="$(dirname "$MANIFEST")/../flatpak/io.github.hyprismteam.HyPrism.yml"
MODULE_FILE="$(dirname "$MANIFEST")/io.github.hyprismteam.HyPrism.module.yml"

if [[ ! -f "$FLATPAK" ]]; then
    echo "error: flatpak header file not found: $FLATPAK" >&2
    exit 1
fi
if [[ ! -f "$MODULE_FILE" ]]; then
    echo "error: module file not found: $MODULE_FILE" >&2
    exit 1
fi

# capture header (everything before the modules: directive)
# use sed to stop before printing the modules: line
header=$(sed '/^modules:/q' "$FLATPAK")
# remove any trailing modules: if accidentally included
header=$(printf '%s' "$header" | sed '/^modules:$/d')

# capture modules section from module file
# replace placeholders on url/sha lines, remove leading blank then drop first line (modules:)
modules=$(sed \
    -e "s|HYPRISM_RELEASE_URL|$url|" \
    -e "s|HYPRISM_RELEASE_SHA256|$sha|" \
    -e "s|HYPRISM_MAIN_BRANCH|$MAIN_SHA|" \
    "$MODULE_FILE" \
  | sed '1{/^$/d}' \
  | sed '1d')

# write combined manifest
{
    printf '%s
' "$header"
    printf 'modules:
'
    printf '%s
' "$modules"
} >"$MANIFEST"

echo "Manifest regenerated and updated: $MANIFEST"