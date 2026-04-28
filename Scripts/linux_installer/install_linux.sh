#!/usr/bin/env bash
set -e

# Функция для вывода сообщений
log() { echo -e "\e[32m[+]\e[0m $1"; }
error() { echo -e "\e[31m[!]\e[0m $1" >&2; exit 1; }

log "Checking dependencies..."
command -v fusermount3 >/dev/null 2>&1 || error "FUSE3 not found. Please install fuse3."

log "Downloading HyPrism installer..."
curl -sSfL -o appimageInstaller \
"https://github.com/HyPrismTeam/HyPrism/releases/latest/download/appimageInstaller"

log "Setting permissions and launching..."
chmod +x appimageInstaller
./appimageInstaller --yes

log "Cleaning up..."
rm appimageInstaller 

log "Done! HyPrism should now run."