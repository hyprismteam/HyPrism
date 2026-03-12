#!/usr/bin/env bash
# ============================================================================
# HyPrism Publish Script
# Packages the Electron.NET application for distribution.
#
# Usage:
#   ./Scripts/publish.sh <target> [<target>...] [--arch x64|arm64]
#
# Targets:
#   all        All formats for the current platform
#   linux      All Linux formats (AppImage + deb + rpm + tar.xz)
#   win        All Windows formats (zip + exe)
#   mac        All macOS formats (dmg)
#   appimage   Linux AppImage
#   deb        Linux .deb package
#   rpm        Linux .rpm package
#   tar        Linux .tar.xz archive
#   flatpak    Linux .flatpak bundle
#   dmg        macOS .dmg disk image
#   zip        Windows portable .zip
#   exe        Windows installer .exe (NSIS)
#   clean      Remove dist/ and intermediate publish dirs
#
# Options:
#   --arch <arch>   Build only for specific architecture (x64 or arm64)
#                   Note: Not all arches are valid for all platforms
#                   - Windows: x64 only
#                   - Linux: x64 and arm64
#                   - macOS: arm64 only (Apple Silicon)
#   --sources <url|path>
#                   Add a NuGet package source when restoring/publishing.
#                   Can be specified multiple times to pass several sources.
#
# Platform restrictions (enforced by Electron.NET):
#   Linux targets  → must build on Linux
#   Windows targets → must build on Windows
#   macOS targets   → must build on macOS
#
# Examples:
#   ./Scripts/publish.sh all                  # All formats, both arches
#   ./Scripts/publish.sh linux                # All Linux, x64 + arm64
#   ./Scripts/publish.sh appimage --arch x64  # AppImage x64 only
#   ./Scripts/publish.sh deb rpm              # deb + rpm, both arches
#   ./Scripts/publish.sh clean                # Clean dist/ and build dirs
# ============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
DIST_DIR="$PROJECT_ROOT/dist"
EB_CONFIG="$PROJECT_ROOT/Properties/electron-builder.json"
LINUX_APP_ID="io.github.hyprismteam.HyPrism"

# ─── Colors ──────────────────────────────────────────────────────────────────
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
BOLD='\033[1m'
NC='\033[0m'

log_info()    { echo -e "${BLUE}▸${NC} $*"; }
log_ok()      { echo -e "${GREEN}✓${NC} $*"; }
log_warn()    { echo -e "${YELLOW}⚠${NC} $*"; }
log_error()   { echo -e "${RED}✗${NC} $*"; }
log_section() { echo -e "\n${BOLD}${CYAN}═══ $* ═══${NC}"; }

# ─── Detect current OS ──────────────────────────────────────────────────────
detect_os() {
    case "$(uname -s)" in
        Linux*)  echo "linux" ;;
        Darwin*) echo "mac" ;;
        MINGW*|MSYS*|CYGWIN*) echo "win" ;;
        *)       echo "unknown" ;;
    esac
}

CURRENT_OS="$(detect_os)"

# ─── Parse arguments ────────────────────────────────────────────────────────
TARGETS=()
ARCH_FILTER=""
SOURCE_ARGS=()
SOURCES=()

# Detect whether we're running inside a Flatpak sandbox.  Flatpak sets a
# few environment variables and creates /run/.flatpak-info.  When sandboxed
# there is no network access, so we must not let `dotnet` fall back to the
# default nuget.org feed; instead we will force restore to use only the
# sources provided via `--sources`.
IN_FLATPAK=0
if [[ -n "$FLATPAK_ID" || -f /run/.flatpak-info ]]; then
    IN_FLATPAK=1
fi

while [[ $# -gt 0 ]]; do
    case "$1" in
        --arch)
            if [[ -z "${2:-}" ]]; then
                log_error "--arch requires an argument (x64 or arm64)"
                exit 1
            fi
            ARCH_FILTER="$2"
            shift 2
            ;;
        --sources)
            if [[ -z "${2:-}" ]]; then
                log_error "--sources requires an argument (URL or path)"
                exit 1
            fi
            # keep a separate list of source values for MSBuild property
            SOURCES+=("$2")
            SOURCE_ARGS+=("--source" "$2")
            shift 2
            ;;
        --help|-h)
            sed -n '/^# ====/,/^# ====/p' "$0" | grep '^#' | sed 's/^# \?//'
            exit 0
            ;;
        *)
            TARGETS+=("$1")
            shift
            ;;
    esac
done

if [[ ${#TARGETS[@]} -eq 0 ]]; then
    log_error "No targets specified."
    echo ""
    echo "Usage: $0 <target> [<target>...] [--arch x64|arm64] [--sources <url|path>]"
    echo ""
    echo "Targets: all linux win mac appimage deb rpm tar flatpak dmg zip exe clean"
    echo ""
    echo "Examples:"
    echo "  $0 all                  # All formats, both arches"
    echo "  $0 appimage --arch x64  # AppImage x64 only"
    echo "  $0 deb rpm              # deb + rpm, both arches"
    echo "  $0 --sources ./nuget-sources all"
    exit 1
fi

# ─── Global variables ────────────────────────────────────────────────────────
BUILD_COUNT=0
FAIL_COUNT=0
SKIP_COUNT=0

# ─── Determine architectures to build ────────────────────────────────────────
# Platform restrictions:
#   - Windows: x64 only
#   - Linux: x64 and arm64
#   - macOS: arm64 only (Apple Silicon)
get_arches() {
    local platform="$1"
    local arches=""

    case "$platform" in
        win)
            arches="x64"
            ;;
        linux)
            arches="x64 arm64"
            ;;
        mac)
            arches="arm64"
            ;;
        *)
            arches="x64"
            ;;
    esac

    # Apply user filter if specified
    if [[ -n "$ARCH_FILTER" ]]; then
        # Only return the arch if it's in the allowed list for the platform
        if [[ " $arches " == *" $ARCH_FILTER "* ]]; then
            echo "$ARCH_FILTER"
        else
            log_warn "Arch '$ARCH_FILTER' not supported for $platform (allowed: $arches)"
            echo ""
        fi
    else
        echo "$arches"
    fi
}

# ─── Check platform compatibility ────────────────────────────────────────────
# Returns 0 if compatible, 1 if not
check_platform() {
    local target_platform="$1"
    if [[ "$CURRENT_OS" != "$target_platform" ]]; then
        return 1
    fi
    return 0
}

platform_name() {
    case "$1" in
        linux) echo "Linux" ;;
        win)   echo "Windows" ;;
        mac)   echo "macOS" ;;
        *)     echo "$1" ;;
    esac
}

# ─── Map platform+arch to .NET RuntimeIdentifier ─────────────────────────────
get_rid() {
    local platform="$1"
    local arch="$2"
    case "${platform}-${arch}" in
        linux-x64)   echo "linux-x64" ;;
        linux-arm64) echo "linux-arm64" ;;
        win-x64)     echo "win-x64" ;;
        win-arm64)   echo "win-arm64" ;;
        mac-x64)     echo "osx-x64" ;;
        mac-arm64)   echo "osx-arm64" ;;
        *) log_error "Unknown platform-arch: ${platform}-${arch}"; return 1 ;;
    esac
}

# ─── Sync mac icon assets from Frontend/public/icon.png ─────────────────────
prepare_macos_icon() {
    local source_png="$PROJECT_ROOT/Frontend/public/icon.png"
    local fallback_png="$PROJECT_ROOT/Build/icon.png"
    local target_png="$PROJECT_ROOT/Build/icon.png"
    local target_icns="$PROJECT_ROOT/Build/icon.icns"

    local icon_source=""
    if [[ -f "$source_png" ]]; then
        icon_source="$source_png"
    elif [[ -f "$fallback_png" ]]; then
        icon_source="$fallback_png"
    else
        log_warn "No source icon found (expected Frontend/public/icon.png or Build/icon.png)"
        return 0
    fi

    cp "$icon_source" "$target_png"

    if ! command -v iconutil >/dev/null 2>&1 || ! command -v sips >/dev/null 2>&1; then
        log_warn "iconutil/sips not available; keeping existing Build/icon.icns"
        return 0
    fi

    local iconset_dir="$PROJECT_ROOT/Build/icon.iconset"
    rm -rf "$iconset_dir"
    mkdir -p "$iconset_dir"

    # Generate Apple iconset sizes
    sips -z 16 16     "$target_png" --out "$iconset_dir/icon_16x16.png" >/dev/null 2>&1
    sips -z 32 32     "$target_png" --out "$iconset_dir/icon_16x16@2x.png" >/dev/null 2>&1
    sips -z 32 32     "$target_png" --out "$iconset_dir/icon_32x32.png" >/dev/null 2>&1
    sips -z 64 64     "$target_png" --out "$iconset_dir/icon_32x32@2x.png" >/dev/null 2>&1
    sips -z 128 128   "$target_png" --out "$iconset_dir/icon_128x128.png" >/dev/null 2>&1
    sips -z 256 256   "$target_png" --out "$iconset_dir/icon_128x128@2x.png" >/dev/null 2>&1
    sips -z 256 256   "$target_png" --out "$iconset_dir/icon_256x256.png" >/dev/null 2>&1
    sips -z 512 512   "$target_png" --out "$iconset_dir/icon_256x256@2x.png" >/dev/null 2>&1
    sips -z 512 512   "$target_png" --out "$iconset_dir/icon_512x512.png" >/dev/null 2>&1
    sips -z 1024 1024 "$target_png" --out "$iconset_dir/icon_512x512@2x.png" >/dev/null 2>&1

    if iconutil -c icns "$iconset_dir" -o "$target_icns" >/dev/null 2>&1; then
        log_ok "Prepared mac icon: Build/icon.icns (from $(basename "$icon_source"))"
    else
        log_warn "Failed to regenerate Build/icon.icns; keeping previous file"
    fi

    rm -rf "$iconset_dir"
}

# ─── Prepare Linux icon set for desktop environments ───────────────────────
prepare_linux_icon_set() {
    local source_png="$PROJECT_ROOT/Frontend/public/icon.png"
    local fallback_png="$PROJECT_ROOT/Build/icon.png"
    local iconset_root="$PROJECT_ROOT/Build/icons"

    local icon_source=""
    if [[ -f "$source_png" ]]; then
        icon_source="$source_png"
    elif [[ -f "$fallback_png" ]]; then
        icon_source="$fallback_png"
    else
        log_warn "No source icon found (expected Frontend/public/icon.png or Build/icon.png)"
        return 0
    fi

    mkdir -p "$PROJECT_ROOT/Build"
    cp "$icon_source" "$PROJECT_ROOT/Build/icon.png"

    rm -rf "$iconset_root"
    mkdir -p "$iconset_root"

    local sizes=(16 24 32 48 64 128 256 512)
    for size in "${sizes[@]}"; do
        local target="$iconset_root/${size}x${size}.png"
        local hicolor_dir="$iconset_root/hicolor/${size}x${size}/apps"
        mkdir -p "$hicolor_dir"

        if command -v convert >/dev/null 2>&1; then
            convert "$PROJECT_ROOT/Build/icon.png" -resize "${size}x${size}" "$target" >/dev/null 2>&1 || cp "$PROJECT_ROOT/Build/icon.png" "$target"
        else
            cp "$PROJECT_ROOT/Build/icon.png" "$target"
        fi

        cp "$target" "$hicolor_dir/${LINUX_APP_ID}.png"
        cp "$target" "$hicolor_dir/HyPrism.png"
    done

    # Keep fallbacks at the icon root as well
    cp "$PROJECT_ROOT/Build/icon.png" "$iconset_root/icon.png"
    cp "$PROJECT_ROOT/Build/icon.png" "$iconset_root/${LINUX_APP_ID}.png"
    cp "$PROJECT_ROOT/Build/icon.png" "$iconset_root/HyPrism.png"

    log_ok "Prepared Linux icon set in Build/icons (hicolor + app-id icons, source: $(basename "$icon_source"))"
}

# ─── Inject AppStream metadata into .deb artifact ───────────────────────────
inject_deb_appstream() {
    local deb_path="$1"
    local metainfo_src="$PROJECT_ROOT/Properties/linux/io.github.hyprismteam.HyPrism.metainfo.xml"

    if [[ ! -f "$metainfo_src" ]]; then
        log_warn "AppStream metadata not found: Properties/linux/io.github.hyprismteam.HyPrism.metainfo.xml"
        return 0
    fi

    if ! command -v dpkg-deb >/dev/null 2>&1; then
        log_warn "dpkg-deb is not available; skipping AppStream injection for $(basename "$deb_path")"
        return 0
    fi

    local tmp_dir
    tmp_dir=$(mktemp -d)

    if ! dpkg-deb -R "$deb_path" "$tmp_dir" >/dev/null 2>&1; then
        log_warn "Failed to unpack $(basename "$deb_path") for AppStream injection"
        rm -rf "$tmp_dir"
        return 0
    fi

    if [[ -f "$tmp_dir/usr/share/metainfo/io.github.hyprismteam.HyPrism.metainfo.xml" ]]; then
        rm -rf "$tmp_dir"
        return 0
    fi

    mkdir -p "$tmp_dir/usr/share/metainfo"
    cp "$metainfo_src" "$tmp_dir/usr/share/metainfo/io.github.hyprismteam.HyPrism.metainfo.xml"

    if dpkg-deb -b "$tmp_dir" "$deb_path" >/dev/null 2>&1; then
        log_ok "Injected AppStream metainfo into $(basename "$deb_path")"
    else
        log_warn "Failed to repack $(basename "$deb_path") after AppStream injection"
    fi

    rm -rf "$tmp_dir"
}

# ─── Inject AppStream metadata into .rpm artifact ───────────────────────────
inject_rpm_appstream() {
    local rpm_path="$1"
    local metainfo_src="$PROJECT_ROOT/Properties/linux/io.github.hyprismteam.HyPrism.metainfo.xml"

    if [[ ! -f "$metainfo_src" ]]; then
        log_warn "AppStream metadata not found: Properties/linux/io.github.hyprismteam.HyPrism.metainfo.xml"
        return 0
    fi

    if ! command -v rpm2cpio >/dev/null 2>&1 || ! command -v cpio >/dev/null 2>&1 || ! command -v rpmbuild >/dev/null 2>&1; then
        log_warn "rpm2cpio/cpio/rpmbuild is not available; skipping AppStream injection for $(basename "$rpm_path")"
        return 0
    fi

    local tmp_dir root_dir
    tmp_dir=$(mktemp -d)
    root_dir="$tmp_dir/root"

    mkdir -p "$root_dir" "$tmp_dir"/{BUILD,BUILDROOT,RPMS,SOURCES,SPECS,SRPMS}

    if ! rpm2cpio "$rpm_path" | (cd "$root_dir" && cpio -idmu --quiet); then
        log_warn "Failed to unpack $(basename "$rpm_path") for AppStream injection"
        rm -rf "$tmp_dir"
        return 0
    fi

    if [[ -f "$root_dir/usr/share/metainfo/io.github.hyprismteam.HyPrism.metainfo.xml" ]]; then
        rm -rf "$tmp_dir"
        return 0
    fi

    mkdir -p "$root_dir/usr/share/metainfo"
    cp "$metainfo_src" "$root_dir/usr/share/metainfo/io.github.hyprismteam.HyPrism.metainfo.xml"

    # Remove build-id symlink tree if present (can conflict with filesystem package on Fedora)
    rm -rf "$root_dir/usr/lib/.build-id"

    # Generate file list with payload files only (avoid owning system directories like /usr or /usr/lib)
    local files_manifest
    files_manifest="$tmp_dir/files.list"
    (
        cd "$root_dir"
        find . -mindepth 1 \( -type f -o -type l \) | LC_ALL=C sort | sed 's#^\.##'
    ) > "$files_manifest"

    local pkg_name pkg_version pkg_release pkg_arch pkg_summary pkg_license
    pkg_name=$(rpm -qp --qf '%{NAME}' "$rpm_path" 2>/dev/null || echo "io.github.hyprismteam.HyPrism")
    pkg_version=$(rpm -qp --qf '%{VERSION}' "$rpm_path" 2>/dev/null || echo "3.0.0")
    pkg_release=$(rpm -qp --qf '%{RELEASE}' "$rpm_path" 2>/dev/null || echo "1")
    pkg_arch=$(rpm -qp --qf '%{ARCH}' "$rpm_path" 2>/dev/null || echo "x86_64")
    pkg_summary=$(rpm -qp --qf '%{SUMMARY}' "$rpm_path" 2>/dev/null || echo "Cross-platform Hytale launcher")
    pkg_license=$(rpm -qp --qf '%{LICENSE}' "$rpm_path" 2>/dev/null || echo "GPL-3.0-only")

    cat > "$tmp_dir/SPECS/repack.spec" <<EOF
Name: $pkg_name
Version: $pkg_version
Release: $pkg_release
Summary: $pkg_summary
License: $pkg_license
BuildArch: $pkg_arch
AutoReqProv: no

%description
HyPrism is a cross-platform Hytale launcher with instance and mod management.

%prep
%build
%install
mkdir -p %{buildroot}
cp -a $root_dir/. %{buildroot}/

%files -f %{_topdir}/files.list
EOF

    if ! rpmbuild --define "_topdir $tmp_dir" -bb "$tmp_dir/SPECS/repack.spec" >/dev/null 2>&1; then
        log_warn "Failed to repack $(basename "$rpm_path") after AppStream injection"
        rm -rf "$tmp_dir"
        return 0
    fi

    local rebuilt_rpm
    rebuilt_rpm=$(find "$tmp_dir/RPMS" -type f -name '*.rpm' | head -n1)
    if [[ -n "$rebuilt_rpm" && -f "$rebuilt_rpm" ]]; then
        cp "$rebuilt_rpm" "$rpm_path"
        log_ok "Injected AppStream metainfo into $(basename "$rpm_path")"
    else
        log_warn "Repacked rpm not found for $(basename "$rpm_path")"
    fi

    rm -rf "$tmp_dir"
}

# ─── Write temporary electron-builder config ──────────────────────────────────
write_config() {
    local platform="$1"
    local targets_json="$2"

    local platform_block=""
    case "$platform" in
        linux)
            platform_block=$(cat <<'INNER'
  "linux": {
    "target": TARGETS_PLACEHOLDER,
    "executableArgs": ["--no-sandbox"],
    "category": "Game",
    "icon": "LINUX_ICONSET_PLACEHOLDER",
        "maintainer": "HyPrism Team <eivordoudo2021@gmail.com>",
        "synopsis": "Cross-platform Hytale launcher",
        "description": "HyPrism is a cross-platform Hytale launcher with instance and mod management.",
    "desktop": {
        "entry": {
            "Name": "HyPrism",
            "Comment": "Cross-platform Hytale launcher",
            "Categories": "Game;Utility;"
        }
    }
  }
INNER
)
            ;;
        win)
            platform_block=$(cat <<'INNER'
  "win": {
    "target": TARGETS_PLACEHOLDER,
        "icon": "icon.ico"
  }
INNER
)
            ;;
        mac)
            platform_block=$(cat <<'INNER'
  "mac": {
    "target": TARGETS_PLACEHOLDER,
    "category": "public.app-category.games",
        "icon": "icon.icns"
  }
INNER
)
            ;;
    esac

    platform_block="${platform_block//TARGETS_PLACEHOLDER/$targets_json}"
    platform_block="${platform_block//LINUX_ICONSET_PLACEHOLDER/$PROJECT_ROOT\/Build\/icons}"

    # Add flatpak-specific config when building flatpak target
    local flatpak_block=""
    if [[ "$targets_json" == *'"flatpak"'* ]]; then
        flatpak_block=$(cat <<'FLATPAK'
,
  "flatpak": {
        "runtimeVersion": "24.08",
        "baseVersion": "24.08",
    "useWaylandFlags": true
  }
FLATPAK
)
    fi

    cat > "$EB_CONFIG" <<EOF
{
  "\$schema": "https://raw.githubusercontent.com/electron-userland/electron-builder/refs/heads/master/packages/app-builder-lib/scheme.json",
  "compression": "store",
  "artifactName": "\${productName}-\${os}-\${arch}-\${version}.\${ext}",
    "directories": {
        "buildResources": "$PROJECT_ROOT/Build",
        "output": "dist"
    },
$platform_block$flatpak_block
}
EOF
}

# ─── Run dotnet publish for one RID and collect artifacts ─────────────────────
do_publish() {
    local rid="$1"
    local label="$2"
    local start_time=$SECONDS

    log_info "Publishing ${BOLD}$label${NC} (${rid})..."

    # Clean previous intermediate build for this RID
    rm -rf "$PROJECT_ROOT/obj/Release/net10.0/$rid/PubTmp" 2>/dev/null || true

    cd "$PROJECT_ROOT"
    local exit_code=0
    # Use -p: instead of /p: for cross-platform compatibility (MSYS/Git Bash on Windows converts /p: to a path)
    local msbuild_args=""
    if [[ $IN_FLATPAK -eq 1 && ${#SOURCES[@]} -gt 0 ]]; then
        # override restore sources so only user-provided locations are used
        local joined
        joined=$(IFS=';'; echo "${SOURCES[*]}")
        msbuild_args="-p:RestoreSources=\"$joined\""
        log_info "Running inside Flatpak; restricting nuget sources to: $joined"
    fi
    dotnet publish -c Release -p:RuntimeIdentifier="$rid" $msbuild_args ${SOURCE_ARGS[@]} || exit_code=$?

    if [[ $exit_code -ne 0 ]]; then
        log_error "Build failed for $label ($rid) — exit code $exit_code"
        FAIL_COUNT=$((FAIL_COUNT + 1))
        return 1
    fi

    local publish_dir="$PROJECT_ROOT/bin/Release/net10.0/$rid/publish"

    if [[ ! -d "$publish_dir" ]]; then
        log_error "No output directory: $publish_dir"
        FAIL_COUNT=$((FAIL_COUNT + 1))
        return 1
    fi

    # Collect distributable artifacts (exclude unpacked dirs and metadata)
    local count=0
    while IFS= read -r -d '' artifact; do
        if [[ "$artifact" == *.deb ]]; then
            inject_deb_appstream "$artifact"
        elif [[ "$artifact" == *.rpm ]]; then
            inject_rpm_appstream "$artifact"
        fi

        cp "$artifact" "$DIST_DIR/"
        count=$((count + 1))
        local size
        size=$(du -h "$artifact" | cut -f1)
        log_ok "  $(basename "$artifact") (${size})"

    done < <(find "$publish_dir" -maxdepth 1 -type f \( \
        -name "*.AppImage" -o \
        -name "*.deb" -o \
        -name "*.rpm" -o \
        -name "*.tar.xz" -o \
        -name "*.flatpak" -o \
        -name "*.dmg" -o \
        -name "*.zip" -o \
        -name "*.exe" \
    \) -print0 2>/dev/null)

    local elapsed=$(( SECONDS - start_time ))
    if [[ $count -gt 0 ]]; then
        log_ok "$label ($rid) — $count artifact(s), ${elapsed}s"
        BUILD_COUNT=$((BUILD_COUNT + count))
    else
        log_warn "$label ($rid) — no artifacts found (${elapsed}s)"
    fi
}

# ─── Build for a platform with multiple arches ────────────────────────────────
build_platform() {
    local platform="$1"
    local targets_json="$2"
    local label="$3"

    # Check platform compatibility
    if ! check_platform "$platform"; then
        local pname
        pname=$(platform_name "$platform")
        log_warn "Skipping $label — requires $pname (current OS: $(platform_name "$CURRENT_OS"))"
        SKIP_COUNT=$((SKIP_COUNT + 1))
        return 0
    fi

    log_section "$label"

    if [[ "$platform" == "mac" ]]; then
        prepare_macos_icon
    elif [[ "$platform" == "linux" ]]; then
        prepare_linux_icon_set
    fi

    write_config "$platform" "$targets_json"

    local arches
    arches=$(get_arches "$platform")
    if [[ -z "$arches" ]]; then
        log_warn "No valid architectures for $platform"
        SKIP_COUNT=$((SKIP_COUNT + 1))
        return 0
    fi

    for arch in $arches; do
        local rid
        rid=$(get_rid "$platform" "$arch")
        do_publish "$rid" "$label [$arch]"
    done
}

# ─── Target definitions ──────────────────────────────────────────────────────

build_appimage() { build_platform "linux" '["AppImage"]'                           "AppImage"; }
build_deb()      { build_platform "linux" '["deb"]'                                 "deb"; }
build_rpm()      { build_platform "linux" '["rpm"]'                                 "rpm"; }
build_tar()      { build_platform "linux" '["tar.xz"]'                              "tar.xz"; }

# Build flatpak using the repository Flatpak manifest (Properties/linux/flatpak/...)
# -- do NOT rely on electron-builder's flatpak target; use flatpak-builder + build-bundle
arch_to_flatpak_arch() {
    case "$1" in
        x64)  echo "x86_64" ;;
        arm64) echo "aarch64" ;;
        *)    echo "$1" ;;
    esac
}

build_flatpak() {
    local platform="linux"

    # Platform check
    if ! check_platform "$platform"; then
        local pname
        pname=$(platform_name "$platform")
        log_warn "Skipping flatpak — requires $pname (current OS: $(platform_name "$CURRENT_OS"))"
        SKIP_COUNT=$((SKIP_COUNT + 1))
        return 0
    fi

    log_section "flatpak"
    prepare_linux_icon_set

    local arches
    arches=$(get_arches "$platform")
    if [[ -z "$arches" ]]; then
        log_warn "No valid architectures for flatpak"
        SKIP_COUNT=$((SKIP_COUNT + 1))
        return 0
    fi

    for arch in $arches; do
        local rid
        rid=$(get_rid "$platform" "$arch")
        do_flatpak_publish "$rid" "$arch"
    done
}

# Publish for a single RID then build .flatpak from Properties/linux/flatpak manifest
do_flatpak_publish() {
    local rid="$1"
    local arch="$2"
    local start_time=$SECONDS

    log_info "Building flatpak for ${BOLD}$arch${NC} (RID: $rid)"

    # dotnet publish (same as do_publish does)
    cd "$PROJECT_ROOT"
    # Ensure dotnet/electron-builder temp files land on disk (avoid small tmpfs /tmp)
    mkdir -p "$PROJECT_ROOT/.tmp"
    export TMPDIR="$PROJECT_ROOT/.tmp"

    local msbuild_args=""
    if [[ $IN_FLATPAK -eq 1 && ${#SOURCES[@]} -gt 0 ]]; then
        local joined
        joined=$(IFS=';'; echo "${SOURCES[*]}")
        msbuild_args="-p:RestoreSources=\"$joined\""
        log_info "(flatpak) restricting nuget sources to: $joined"
    fi

    if ! dotnet publish -c Release -p:RuntimeIdentifier="$rid" $msbuild_args ${SOURCE_ARGS[@]}; then
        log_error "dotnet publish failed for $rid"
        FAIL_COUNT=$((FAIL_COUNT + 1))
        return 1
    fi

    local publish_dir="$PROJECT_ROOT/bin/Release/net10.0/$rid/publish"
    if [[ ! -d "$publish_dir" ]]; then
        log_error "No publish output at $publish_dir"
        FAIL_COUNT=$((FAIL_COUNT + 1))
        return 1
    fi

    # Copy publish output into manifest 'bundle' source (manifest expects a 'bundle' dir)
    local manifest_dir="$PROJECT_ROOT/Properties/linux/flatpak"
    local bundle_dir="$manifest_dir/bundle"
    local manifest_name="io.github.hyprismteam.HyPrism.yml"
    rm -rf "$bundle_dir"
    mkdir -p "$bundle_dir"
    cp -a "$publish_dir/." "$bundle_dir/"

    # Ensure flatpak manifest can find the executable when electron-builder places
    # the binary under linux-unpacked/ (copy it to bundle/HyPrism for compatibility)
    if [[ -x "$bundle_dir/linux-unpacked/HyPrism" ]]; then
        cp -a "$bundle_dir/linux-unpacked/HyPrism" "$bundle_dir/HyPrism"
        chmod +x "$bundle_dir/HyPrism" || true
    fi

    # Build local flatpak repo and export .flatpak
    local repo_dir="$DIST_DIR/flatpak-repo-$arch"
    rm -rf "$repo_dir"
    mkdir -p "$repo_dir"

    local build_dir="$PROJECT_ROOT/.flatpak-builder/build-flatpak-$arch"
    rm -rf "$build_dir"
    mkdir -p "$build_dir"

    if ! (cd "$manifest_dir" && flatpak-builder --force-clean --repo="$repo_dir" --install-deps-from=flathub --install-deps-from=flathub-beta "$build_dir" "$manifest_name"); then
        log_error "flatpak-builder failed for $arch"
        rm -rf "$bundle_dir" "$build_dir"
        FAIL_COUNT=$((FAIL_COUNT + 1))
        return 1
    fi

    # Version from project file
    local version
    version=$(grep -oP '<Version>\K[^<]+' "$PROJECT_ROOT/HyPrism.csproj" || echo "0.0.0")

    local flatpak_arch
    flatpak_arch=$(arch_to_flatpak_arch "$arch")
    local out_file="$DIST_DIR/HyPrism-linux-$arch-$version.flatpak"

    # Use the app-id declared in the Flatpak manifest so repo refs match the bundle export
    local manifest_file="$manifest_dir/io.github.hyprismteam.HyPrism.yml"
    local flatpak_app_id
    flatpak_app_id=$(grep -E '^app-id:' "$manifest_file" | awk '{print $2}' | tr -d "\"'" ) || flatpak_app_id="io.github.hyprismteam.HyPrism"

    flatpak build-bundle "$repo_dir" "$out_file" "$flatpak_app_id" --arch="$flatpak_arch"

    local elapsed=$(( SECONDS - start_time ))
    log_ok "Flatpak built: $(basename "$out_file") — ${elapsed}s"
    BUILD_COUNT=$((BUILD_COUNT + 1))

    # cleanup temporary bundle & build dir (keep repo for inspection)
    rm -rf "$bundle_dir" "$build_dir"
}

build_dmg()      { build_platform "mac"   '["dmg"]'                                 "dmg"; }
build_zip()      { build_platform "win"   '["zip"]'                                 "zip"; }
build_exe()      { build_platform "win"   '["nsis"]'                                "exe (NSIS)"; }

build_linux()    { build_platform "linux" '["AppImage", "deb", "rpm", "tar.xz"]'    "Linux (all formats)"; }
build_win()      { build_platform "win"   '["zip", "nsis"]'                        "Windows (zip + exe)"; }
build_mac()      { build_platform "mac"   '["dmg"]'                                 "macOS (dmg)"; }

build_all() {
    build_linux
    build_win
    build_mac
}

# ─── Clean ────────────────────────────────────────────────────────────────────
do_clean() {
    log_section "Cleaning"
    rm -rf "$DIST_DIR"
    log_ok "Removed dist/"

    # Clean intermediate publish dirs for all RIDs
    local rids=(linux-x64 linux-arm64 win-x64 win-arm64 osx-x64 osx-arm64)
    for rid in "${rids[@]}"; do
        local pub_tmp="$PROJECT_ROOT/obj/Release/net10.0/$rid/PubTmp"
        local pub_dir="$PROJECT_ROOT/bin/Release/net10.0/$rid/publish"
        if [[ -d "$pub_tmp" ]]; then
            rm -rf "$pub_tmp"
            log_ok "Removed obj/.../$rid/PubTmp"
        fi
        if [[ -d "$pub_dir" ]]; then
            rm -rf "$pub_dir"
            log_ok "Removed bin/.../$rid/publish"
        fi
    done
    log_ok "Clean complete"
}

# ─── Main ─────────────────────────────────────────────────────────────────────

# Handle clean separately (no config backup needed)
if [[ "${TARGETS[0]}" == "clean" ]]; then
    do_clean
    exit 0
fi

# Save & restore electron-builder.json
EB_CONFIG_BAK=$(mktemp)
cp "$EB_CONFIG" "$EB_CONFIG_BAK" 2>/dev/null || true
trap 'cp "$EB_CONFIG_BAK" "$EB_CONFIG" 2>/dev/null; rm -f "$EB_CONFIG_BAK"' EXIT

mkdir -p "$DIST_DIR"
TOTAL_START=$SECONDS

log_section "HyPrism Publish"
log_info "OS: $(platform_name "$CURRENT_OS")"
log_info "Targets: ${TARGETS[*]}"
[[ -n "$ARCH_FILTER" ]] && log_info "Arch: $ARCH_FILTER" || log_info "Arch: x64 + arm64"
log_info "Output: $DIST_DIR/"

for target in "${TARGETS[@]}"; do
    case "$target" in
        all)       build_all ;;
        linux)     build_linux ;;
        win)       build_win ;;
        mac)       build_mac ;;
        appimage)  build_appimage ;;
        deb)       build_deb ;;
        rpm)       build_rpm ;;
        tar)       build_tar ;;
        flatpak)   build_flatpak ;;
        dmg)       build_dmg ;;
        zip)       build_zip ;;
        exe)       build_exe ;;
        *)
            log_error "Unknown target: $target"
            echo "Valid targets: all linux win mac appimage deb rpm tar flatpak dmg zip exe clean"
            exit 1
            ;;
    esac
done

TOTAL_ELAPSED=$(( SECONDS - TOTAL_START ))

log_section "Summary"
log_info "Time: ${TOTAL_ELAPSED}s"
[[ $BUILD_COUNT -gt 0 ]] && log_ok "Artifacts: $BUILD_COUNT"
[[ $SKIP_COUNT -gt 0 ]]  && log_warn "Skipped: $SKIP_COUNT (wrong platform)"
[[ $FAIL_COUNT -gt 0 ]]  && log_error "Failed: $FAIL_COUNT"

if [[ $BUILD_COUNT -gt 0 ]]; then
    echo ""
    log_info "Artifacts in ${BOLD}$DIST_DIR/${NC}:"
    ls -lhS "$DIST_DIR/" 2>/dev/null | tail -n +2
fi

[[ $FAIL_COUNT -gt 0 ]] && exit 1
exit 0
