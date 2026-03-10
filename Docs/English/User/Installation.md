# Installation

## Requirements

- **Windows:** Windows 10 **22H2** or Windows 11 (x64)
- **Linux:** x64 distribution compatible with modern Electron builds (tested baseline: Ubuntu 18.04+, Debian 10+, Fedora 32+)
- **macOS:** Monterey (12)+, Apple Silicon (arm64)
- Internet connection for first launch (authentication & game download)

Runtime note:
- HyPrism is currently built with **Electron 34.2.0** and `net10.0` self-contained publish.
- No separate .NET runtime installation is required for release binaries.
- On old Windows 10 builds (pre-22H2), startup can fail due to unsupported OS/runtime combinations.

## Download

Download the latest release from the [GitHub Releases](https://github.com/yyyumeniku/HyPrism/releases) page.

### Windows

Option A (portable):
1. Download `HyPrism-win-x64.zip`
2. Extract to any folder
3. Run `HyPrism.exe`

Option B (installer):
1. Download `HyPrism-win-x64-<version>.exe`
2. Run installer
3. Launch HyPrism from Start menu or desktop shortcut

### Linux

HyPrism uses native Linux dialog tools for folder/file selection. Install at least one of:
- `zenity`
- `kdialog`
- `yad`
- `qarma`

#### AppImage
1. Download `HyPrism-linux-x64.AppImage`
2. Make executable: `chmod +x HyPrism-linux-x64.AppImage`
3. Run: `./HyPrism-linux-x64.AppImage`

#### Flatpak
```bash
flatpak install io.github.hyprismteam.HyPrism
flatpak run io.github.hyprismteam.HyPrism
```

Note: the Flatpak includes a small launcher wrapper that checks your per-user data directory (`$XDG_DATA_HOME/HyPrism` or `~/.local/share/HyPrism`) for an installed HyPrism release and runs it if present. If no release is found the wrapper will download the Linux release artifact from GitHub (it tries the `latest` release first, and falls back to the latest prerelease if no suitable asset exists), extract it into the app data directory, and then execute the launcher. Actions and errors are logged to `XDG_DATA_HOME/HyPrism/wrapper.log`. This behavior uses the Flatpak per‑app data area and requires no additional filesystem permissions.

Sandboxing: the Flatpak build uses `zypak` (when provided by the base runtime) to redirect Chromium's SUID `chrome-sandbox` into the Flatpak sandbox. When `zypak` is available the launcher will use the bundled `chrome-sandbox` normally; when it isn't available the wrapper falls back to running the binary with `--no-sandbox`. For troubleshooting, set `ZYPAK_DEBUG=1` or `ZYPAK_STRACE=all`. The Flatpak base `org.electronjs.Electron2.BaseApp` (21.08+) includes zypak in recent releases.

### macOS

1. Download `HyPrism-osx-x64.zip` (or `osx-arm64` for Apple Silicon)
2. Extract and move `HyPrism.app` to Applications
3. Launch from Applications

## Data Directory

HyPrism stores its data (config, instances, profiles, logs) in:

| OS | Path |
|----|------|
| Windows | `%APPDATA%/HyPrism/` |
| Linux | `~/.local/share/HyPrism/` |
| macOS | `~/Library/Application Support/HyPrism/` |

### Directory Structure

```
HyPrism/
├── config.json         # Launcher configuration
├── Instances/          # Game installations grouped by branch/version
│   └── release/
│       ├── v8/         # Individual versioned instance
│       └── latest/     # Latest-tracked instance
├── Profiles/           # Player profiles and skin backups
├── Logs/               # Application logs
└── Cache/              # Temporary files
```

## First Launch (Onboarding)

On first launch, HyPrism guides you through setup with an onboarding wizard:

1. **Splash Screen** — Welcome to HyPrism
2. **Language Selection** — Choose your preferred language (12 available)
3. **Hytale Authentication** — Log in with your Hytale account
4. **Profile Setup** — Create your first player profile (nickname, avatar)
5. **Initial Settings** — Configure GPU preference and other options

After onboarding:
- The launcher creates the data directory structure
- Your profile and settings are saved
- You can download and install the game from the Dashboard
