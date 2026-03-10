#!/bin/sh
#
# Launcher wrapper for Flatpak — prefers zypak when available to support chrome-sandbox.
# When zypak is present the wrapper runs the Electron binary via `zypak-wrapper` and
# sets `ZYPAK_SANDBOX_FILENAME=chrome-sandbox` if the bundled sandbox binary exists.
# If zypak is not available the wrapper falls back to running the binary with
# `--no-sandbox` for maximum compatibility. Use `ZYPAK_DEBUG=1` or `ZYPAK_STRACE=all`
# for troubleshooting.
#
# HyPrism Flatpak launcher wrapper —
# - if a user‑installed copy exists in $XDG_DATA_HOME/HyPrism, run it
# - otherwise download the Linux release (latest → prerelease), extract to app data dir and run
# - fall back to bundled /app/HyPrism/HyPrism if anything fails

set -eu

DATA_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/HyPrism"
LOG="$DATA_DIR/wrapper.log"
mkdir -p "$DATA_DIR"

# canonical launcher paths used by the wrapper
# - DYNAMIC_LAUNCHER = user‑managed / updatable launcher in XDG_DATA_HOME
# - BUNDLED_LAUNCHER  = bundled internal launcher shipped inside the Flatpak
DYNAMIC_LAUNCHER="$DATA_DIR/HyPrism"
BUNDLED_LAUNCHER="/app/HyPrism/HyPrism"
BUNDLED_WRAPPER="/app/HyPrism/hyprism-launcher-wrapper.sh"
BUNDLED_DIR="/app/HyPrism"

# helper to launch bundled launcher and gracefully handle chrome-sandbox issues
launch_bundled() {
  # accept explicit args or fall back to FORWARD_ARGS
  local extra_args
  if [ "$#" -gt 0 ]; then
    extra_args="$*"
  else
    extra_args=$FORWARD_ARGS
  fi
  log "Preparing to launch bundled launcher: $BUNDLED_LAUNCHER"

  # Prefer zypak (redirects Chromium sandbox into Flatpak sandbox) when available.
  if command -v zypak-wrapper >/dev/null 2>&1; then
    if [ -x /app/HyPrism/chrome-sandbox ]; then
      export ZYPAK_SANDBOX_FILENAME=chrome-sandbox
      log "zypak-wrapper available — using ZYPAK_SANDBOX_FILENAME=chrome-sandbox"
    else
      log "zypak-wrapper available — no chrome-sandbox found in /app/HyPrism"
    fi
    # run via zypak-wrapper (passes through arguments)
    eval exec zypak-wrapper \"$BUNDLED_LAUNCHER\" $extra_args
  else
    # Fallback: run bundled HyPrism with --no-sandbox (older/no-zypak hosts)
    eval \"$BUNDLED_LAUNCHER\" --no-sandbox $extra_args
  fi
}

log() {
  ts="$(date '+%Y-%m-%d %H:%M:%S %Z')"
  printf "%s %s\n" "$ts" "$*" >> "$LOG"
  printf "%s %s\n" "$ts" "$*" >&2
}

# prefer Wayland when available
export ELECTRON_ENABLE_WAYLAND=1
if [ -n "${WAYLAND_DISPLAY:-}" ]; then
  log "ELECTRON_ENABLE_WAYLAND=1 (host reports WAYLAND_DISPLAY=$WAYLAND_DISPLAY) — Wayland available"
elif [ -n "${DISPLAY:-}" ]; then
  log "ELECTRON_ENABLE_WAYLAND=1 but no WAYLAND_DISPLAY found; DISPLAY is $DISPLAY — X11/fallback will be used"
else
  log "ELECTRON_ENABLE_WAYLAND=1 but no WAYLAND_DISPLAY or DISPLAY detected — display backend unknown"
fi

log "Wrapper start — DATA_DIR=$DATA_DIR"

# Parse wrapper-only flags (these alter wrapper behaviour and are NOT forwarded)
FORCE_BUNDLED=0
SKIP_UPDATE=0
SHOW_HELP=0
FORWARD_ARGS=""
for _arg in "$@"; do
  case "$_arg" in
    --force-bundled) FORCE_BUNDLED=1 ;;
    --skip-update) SKIP_UPDATE=1 ;;
    -h|--help) SHOW_HELP=1 ;;
    *) esc=$(printf '%s' "$_arg" | sed 's/"/\\"/g'); FORWARD_ARGS="$FORWARD_ARGS \"$esc\"" ;;
  esac
done

if [ "$SHOW_HELP" -eq 1 ]; then
  cat <<'USAGE' >&2
Usage: hyprism-launcher-wrapper.sh [--force-bundled] [--skip-update] [--help|-h] [-- <args>]

Options:
  --force-bundled   Skip checks and launch the bundled internal launcher (bundled)
  --skip-update     Skip online/version checks and launch the user-installed launcher (dynamic)
  -h, --help        Show this help and exit

All other arguments are forwarded to the HyPrism binary.
USAGE
  exit 0
fi

if [ "$FORCE_BUNDLED" -eq 1 ]; then
  log "--force-bundled: skipping checks and launching bundled internal launcher"
  if [ -x "$BUNDLED_LAUNCHER" ]; then
    launch_bundled
  else
    log "--force-bundled requested but internal launcher not found — exiting"
    echo "Internal launcher not found" >&2
    exit 1
  fi
fi

if [ "$SKIP_UPDATE" -eq 1 ]; then
  log "--skip-update: skipping online/version checks and launching user-installed launcher if present"
  if [ -x "$DYNAMIC_LAUNCHER" ]; then
    log "Launching user-installed launcher: $DYNAMIC_LAUNCHER"
    if command -v zypak-wrapper >/dev/null 2>&1; then
      if [ -x /app/HyPrism/chrome-sandbox ]; then export ZYPAK_SANDBOX_FILENAME=chrome-sandbox; fi
      eval exec zypak-wrapper \"$DYNAMIC_LAUNCHER\" $FORWARD_ARGS
    else
      eval exec \"$DYNAMIC_LAUNCHER\" --no-sandbox $FORWARD_ARGS
    fi
  else
    log "No user-installed launcher found for --skip-update — falling back to bundled launcher"
    if [ -x "$BUNDLED_LAUNCHER" ]; then
      launch_bundled
    else
      log "No launcher available for --skip-update — exiting"
      echo "No launcher available" >&2
      exit 1
    fi
  fi
fi

# Defer checking/running a user-installed launcher until we know the remote latest version.
# This enables automatic updates: if a newer release exists on GitHub the wrapper will
# download and replace the installed copy before launching.
# (The actual check happens after querying GitHub releases.)

# Determine asset name by architecture
case "$(uname -m)" in
  x86_64|amd64) ASSET_RE='HyPrism.*linux.*\\.tar.*' ;;
  aarch64|arm64) ASSET_RE='HyPrism.*linux.*\\.tar.*' ;;
  *) ASSET_RE='HyPrism.*linux.*\\.tar.*' ;;
esac

# Helper: get browser_download_url for matching asset from GitHub API JSON
get_asset_url() {
  local json="$1" asset
  asset=$(printf "%s" "$json" | grep -E '"browser_download_url"' | sed -E 's/.*"browser_download_url" *: *"([^"]+)".*/\1/' | grep -E "$ASSET_RE" | head -n1 || true)
  printf "%s" "$asset"
}

# Downloader (curl/wget)
download_file() {
  local url="$1" out="$2"
  if command -v curl >/dev/null 2>&1; then
    curl -L --fail --silent --show-error -o "$out" "$url"
    return $?
  elif command -v wget >/dev/null 2>&1; then
    wget -qO "$out" "$url"
    return $?
  else
    return 2
  fi
}

# Helper: normalize a GitHub tag (strip leading v/V, beta- prefixes, trailing metadata)
normalize_version() {
  # examples: v1.2.3 -> 1.2.3    beta3-3.0.0 -> 3.0.0
  printf '%s' "$1" | sed -E 's/^[vV]//; s/^beta[^-]*-//; s/[^0-9.].*$//'
}

# Return 0 if version $1 is less than version $2 (semantic numeric compare)
version_lt() {
  [ "$1" = "$2" ] && return 1
  local i ai bi
  for i in 1 2 3 4 5; do
    ai=$(printf '%s' "$1" | cut -d. -f$i)
    bi=$(printf '%s' "$2" | cut -d. -f$i)
    ai=${ai:-0}
    bi=${bi:-0}
    ai=$(printf '%s' "$ai" | sed 's/[^0-9].*//')
    bi=$(printf '%s' "$bi" | sed 's/[^0-9].*//')
    ai=${ai:-0}
    bi=${bi:-0}
    if [ "$ai" -lt "$bi" ]; then return 0; fi
    if [ "$ai" -gt "$bi" ]; then return 1; fi
  done
  return 1
}

# Try GitHub API: latest → prereleases
REPO="hyprismteam/HyPrism"
log "Looking for release asset matching: $ASSET_RE"
asset_url=""

# try latest
LATEST_API_URL="https://api.github.com/repos/$REPO/releases/latest"
log "Querying GitHub API: $LATEST_API_URL"
if command -v curl >/dev/null 2>&1; then
  json=$(curl -sS --fail "$LATEST_API_URL" 2>/dev/null || true)
elif command -v wget >/dev/null 2>&1; then
  json=$(wget -qO- "$LATEST_API_URL" 2>/dev/null || true)
else
  json=""
fi
if [ -n "$json" ]; then
  asset_url=$(get_asset_url "$json")
  log "Asset URL in latest release: ${asset_url:-<none>}"
  # extract tag_name (e.g. "v2.3.4") and normalize to "2.3.4"
  REMOTE_TAG=$(printf "%s" "$json" | grep -E '"tag_name"' | sed -E 's/.*"tag_name" *: *"([^\"]+)".*/\1/' | head -n1 || true)
  REMOTE_VERSION=$(normalize_version "$REMOTE_TAG")
else
  log "No JSON response from $LATEST_API_URL"
fi

if [ -n "$REMOTE_VERSION" ]; then
  log "upstream version is $REMOTE_VERSION"
else
  log "Could not determine latest upstream release"
fi

# fallback: search releases for first prerelease with matching asset
if [ -z "$asset_url" ]; then
  ALL_API_URL="https://api.github.com/repos/$REPO/releases"
  log "Querying GitHub API: $ALL_API_URL"
  if command -v curl >/dev/null 2>&1; then
    json_all=$(curl -sS --fail "$ALL_API_URL" 2>/dev/null || true)
  elif command -v wget >/dev/null 2>&1; then
    json_all=$(wget -qO- "$ALL_API_URL" 2>/dev/null || true)
  else
    json_all=""
  fi
  if [ -n "$json_all" ]; then
      # prefer non-draft releases; pick first release with matching asset
      asset_url=$(get_asset_url "$json_all")
      if [ -n "$asset_url" ]; then
        # find the tag_name for the release that contains the matched asset
        lineno=$(grep -nF "$asset_url" <<JSON | head -n1 | cut -d: -f1 || true
$json_all
JSON
        )
        if [ -n "$lineno" ]; then
          REMOTE_TAG=$(printf "%s" "$json_all" | head -n "$lineno" | grep -E '"tag_name"' | tail -n1 | sed -E 's/.*"tag_name" *: *"([^"]+)".*/\1/' || true)
          REMOTE_VERSION=$(normalize_version "$REMOTE_TAG")
        fi
      fi
  else
    log "No JSON response from $ALL_API_URL"
  fi
fi

# If a user-installed launcher exists, check its version and auto-update if a newer
# release is available on GitHub. If we cannot determine versions, fall back to running
# the installed binary.
log "Checking for user-installed HyPrism at $DYNAMIC_LAUNCHER"
if [ -x "$DYNAMIC_LAUNCHER" ]; then
  log "Found user-installed launcher: $DYNAMIC_LAUNCHER"
  INSTALLED_VER=""
  if [ -f "$DATA_DIR/version.txt" ]; then
    INSTALLED_VER=$(sed -n '1p' "$DATA_DIR/version.txt" 2>/dev/null || true)
    INSTALLED_VER=$(normalize_version "$INSTALLED_VER")
    if [ -n "$INSTALLED_VER" ]; then
      log "found version $INSTALLED_VER"
    fi
  else
    # Try common CLI version flags; these are best-effort and optional.
    if "$DYNAMIC_LAUNCHER" --version >/dev/null 2>&1; then
      INSTALLED_VER=$("$DYNAMIC_LAUNCHER" --version 2>/dev/null | head -n1 | sed -E 's/[^0-9.].*$//' || true)
    elif "$DYNAMIC_LAUNCHER" -v >/dev/null 2>&1; then
      INSTALLED_VER=$("$DYNAMIC_LAUNCHER" -v 2>/dev/null | head -n1 | sed -E 's/[^0-9.].*$//' || true)
    fi
    if [ -n "$INSTALLED_VER" ]; then
      INSTALLED_VER=$(normalize_version "$INSTALLED_VER")
      printf "%s\n" "$INSTALLED_VER" > "$DATA_DIR/version.txt" 2>/dev/null || true
      log "found version $INSTALLED_VER"
    fi
  fi

  # If we know both versions, compare and update if needed.
  if [ -n "$REMOTE_VERSION" ] && [ -n "$INSTALLED_VER" ]; then
    if version_lt "$INSTALLED_VER" "$REMOTE_VERSION"; then
      log "Installed launcher version $INSTALLED_VER is older than latest $REMOTE_VERSION — will update"
      # continue to download/extract branch below
    else
      log "Installed launcher is up-to-date ($INSTALLED_VER) — launching $DYNAMIC_LAUNCHER"
      if command -v zypak-wrapper >/dev/null 2>&1; then
        if [ -x /app/HyPrism/chrome-sandbox ]; then export ZYPAK_SANDBOX_FILENAME=chrome-sandbox; fi
        exec zypak-wrapper "$DYNAMIC_LAUNCHER" "$@"
      else
        exec "$DYNAMIC_LAUNCHER" --no-sandbox "$@"
      fi
    fi
  else
    log "Found user release at $DYNAMIC_LAUNCHER (version unknown) — launching $DYNAMIC_LAUNCHER"
    if command -v zypak-wrapper >/dev/null 2>&1; then
      if [ -x /app/HyPrism/chrome-sandbox ]; then export ZYPAK_SANDBOX_FILENAME=chrome-sandbox; fi
      exec zypak-wrapper "$DYNAMIC_LAUNCHER" "$@"
    else
      exec "$DYNAMIC_LAUNCHER" --no-sandbox "$@"
    fi
  fi
fi

if [ -z "$asset_url" ]; then
  log "No suitable GitHub release asset found; falling back to bundled launcher"
  if [ -x "$BUNDLED_LAUNCHER" ]; then
    log "Launching bundled launcher: $BUNDLED_LAUNCHER"
    launch_bundled "$@"
  fi
  log "Bundled launcher missing — exiting"
  echo "No launcher available" >&2
  exit 1
fi

log "Downloading asset: $asset_url"
TMP_TAR="$DATA_DIR/hyprism-release.tar.xz"
rm -f "$TMP_TAR"
if ! download_file "$asset_url" "$TMP_TAR"; then
  log "Download failed: $asset_url — falling back to bundled launcher"
  if [ -x "$BUNDLED_LAUNCHER" ]; then
    log "Launching bundled launcher: $BUNDLED_LAUNCHER"
    launch_bundled "$@"
  fi
  exit 1
fi

log "Extracting release to $DATA_DIR"
# Extract into a temporary dir then move files
TMP_DIR="$DATA_DIR/.extract.$$"
rm -rf "$TMP_DIR" && mkdir -p "$TMP_DIR"
if tar -xJf "$TMP_TAR" -C "$TMP_DIR" 2>>"$LOG"; then
  # find top-level dir containing HyPrism binary
  found_bin=$(find "$TMP_DIR" -type f -name HyPrism -perm /111 | head -n1 || true)
  if [ -n "$found_bin" ]; then
    rm -rf "$DATA_DIR"/* || true
    mkdir -p "$DATA_DIR"
    # copy extracted tree into DATA_DIR preserving structure
    cp -a "$TMP_DIR"/* "$DATA_DIR/" 2>>"$LOG" || true
    chmod +x "$DYNAMIC_LAUNCHER" 2>>"$LOG" || true
    # save normalized remote version so wrapper can check before next run
    if [ -n "$REMOTE_VERSION" ]; then
      printf "%s\n" "$REMOTE_VERSION" > "$DATA_DIR/version.txt" 2>>"$LOG" || true
    fi
    rm -rf "$TMP_DIR" "$TMP_TAR"
    log "Extraction complete — exec $DYNAMIC_LAUNCHER"
    if command -v zypak-wrapper >/dev/null 2>&1; then
      if [ -x /app/HyPrism/chrome-sandbox ]; then export ZYPAK_SANDBOX_FILENAME=chrome-sandbox; fi
      exec zypak-wrapper "$DYNAMIC_LAUNCHER" "$@"
    else
      exec "$DYNAMIC_LAUNCHER" --no-sandbox "$@"
    fi
  else
    log "No HyPrism binary found inside archive — falling back"
    rm -rf "$TMP_DIR" "$TMP_TAR"
    if [ -x "$BUNDLED_LAUNCHER" ]; then
      log "Launching bundled launcher: $BUNDLED_LAUNCHER"
      launch_bundled "$@"
    fi
    exit 1
  fi
else
  log "Extraction failed" 
  rm -rf "$TMP_DIR" "$TMP_TAR"
  if [ -x "$BUNDLED_LAUNCHER" ]; then
    log "Launching bundled launcher: $BUNDLED_LAUNCHER"
    launch_bundled "$@"
  fi
  exit 1
fi

# Minimal shim that execs the wrapper shipped in /app/HyPrism if present,
# otherwise execs the system wrapper (this file is kept to make bundle builds
# include the wrapper script). The real logic is in Properties/linux/flatpak/hyprism-launcher-wrapper.sh

if [ -x "$BUNDLED_WRAPPER" ]; then
  log "Delegating to $BUNDLED_WRAPPER"
  exec "$BUNDLED_WRAPPER" "$@"
fi

# Fallback to bundled binary
if [ -x "$BUNDLED_LAUNCHER" ]; then
  log "Launching bundled launcher: $BUNDLED_LAUNCHER"
  launch_bundled "$@"
fi

# Last-resort: fail with message
log "HyPrism launcher not available inside bundle — exiting"
echo "HyPrism launcher not available inside bundle" >&2
exit 1
