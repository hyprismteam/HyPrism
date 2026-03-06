# Services Reference

All services are registered as singletons in `Bootstrapper.cs` and injected via constructor.

## Core Services (`Services/Core/`)

### IpcService
- **File:** `Services/Core/Ipc/IpcService.cs`
- **Purpose:** Central IPC channel registry — single source of truth for all React ↔ .NET communication
- **Key method:** `RegisterAll()` — registers all IPC handlers
- **Annotations:** Contains `@type` and `@ipc` doc comments used by code generator
- **Domains:** config, game, news, profile, settings, update, i18n, window, browser, mods, console
- **Instance saves handlers:** supports listing saves, opening save folders, and deleting save folders via IPC (`hyprism:instance:saves`, `hyprism:instance:openSaveFolder`, `hyprism:instance:deleteSave`)
- **Folder picker timeout:** `hyprism:file:browseFolder` uses extended timeout (300s) to allow manual directory selection without frontend timeout.
- **Mods target resolution:** mod IPC handlers resolve the target from installed instance metadata (including latest) and avoid implicit `branch/latest` placeholder fallback.
- **Mods exact targeting:** mod IPC accepts optional `instanceId`; when provided, it has priority over branch/version to prevent collisions between multiple instances with the same version.
- **Mods changelog:** `hyprism:mods:changelog` returns the (best-effort) plaintext changelog for a specific CurseForge mod file (`modId` + `fileId`).
- **Instance operations targeting:** instance delete/saves IPC handlers accept `instanceId` and resolve by GUID first, with branch/version kept only as backward-compatible fallback.
- **Instance icon refresh:** `hyprism:instance:getIcon` returns a cache-busted file URL (`?v=<lastWriteTicks>`) so updated logos appear immediately after overwrite.
- **Frontend icon loading rule:** instance list icon requests are executed sequentially (not in parallel) to avoid mixed responses on shared IPC reply channels.
- **Launcher updater IPC:**
  - `hyprism:update:available` (event) — emitted when a newer launcher version is found; includes current/latest + changelog.
  - `hyprism:update:check` (invoke) — triggers a manual update check.
  - `hyprism:update:install` (invoke) — downloads and applies the update (self-replace + restart).

### ConfigService
- **File:** `Services/Core/ConfigService.cs`
- **Type:** Singleton
- **Purpose:** Application configuration (persisted to JSON)
- **Config paths:**
  - Windows: `%APPDATA%/HyPrism/config.json`
  - Linux: `~/.config/HyPrism/config.json`
  - macOS: `~/Library/Application Support/HyPrism/config.json`

### Logger
- **File:** `Services/Core/Logger.cs`
- **Type:** Static class
- **Purpose:** Structured logging (Serilog backend + colored console + in-memory buffer)
- **Methods:** `Info()`, `Success()`, `Warning()`, `Error()`, `Debug()`, `Progress()`
- **Log files:** `{appDir}/Logs/{timestamp}.log`

### LocalizationService
- **File:** `Services/Core/LocalizationService.cs`
- **Type:** Singleton (Instance pattern)
- **Purpose:** Runtime language switching with nested key support
- **Locale files:** `Assets/Locales/{code}.json`

### BrowserService
- **File:** `Services/Core/BrowserService.cs`
- **Purpose:** Opens URLs in the system default browser

### DiscordService
- **File:** `Services/Core/DiscordService.cs`
- **Purpose:** Discord Rich Presence integration

### GitHubService
- **File:** `Services/Core/GitHubService.cs`
- **Purpose:** Release checking and self-update functionality

### UpdateService
- **File:** `Services/Core/App/UpdateService.cs`
- **Purpose:** Checks GitHub Releases for a newer launcher version and applies a self-update.
- **Update source:** `yyyumeniku/TEST` (GitHub Releases API)
- **User flow:** on startup, if a newer version exists, the dashboard shows an update indicator. Installing the update downloads quietly in-app (with progress), replaces the launcher executable/app, and restarts.
- **Fallback:** update assets are downloaded into the user **Downloads** folder when available, so users can manually install if auto-update fails.
- **Windows portable updates:** when updating from a `.zip`, the updater copies the extracted app folder (including side-by-side runtime files like `ffmpeg.dll`) into the install directory.

## Game Services (`Services/Game/`)

### GameSessionService
- **Purpose:** Manages game lifecycle — download, install, patch, launch
- **States:** preparing → download → install → patching → launching → running → stopped
- **Auth launch behavior:** In authenticated mode, launch identity/name is derived from token claims when available to avoid server-side username mismatch shutdowns.
- **Custom auth mode:** Non-official profiles can launch in online authenticated mode with client binary patching for custom session domains. Server authentication uses one of two modes controlled by the `UseDualAuth` setting.
- **Server patching modes:**
  - **DualAuth (default):** Only the client binary is patched; the server JAR is left unmodified. A runtime Java Agent (`dualauth-agent.jar`) is downloaded from GitHub and injected via `-javaagent:` environment variable to handle server authentication at runtime. Before each launch, `DualAuthService.EnsureAgentUpToDateAsync()` checks the GitHub Releases API for a newer version and downloads the update automatically, falling back to the local file if GitHub is unreachable. The installed version is tracked in `DualAuth/.agent-version`.
  - **Legacy JAR patching (opt-in):** Enabled via the `Legacy Patching` toggle in Settings. `ClientPatcher.EnsureAllPatched()` statically patches both the client binary and `Server/HytaleServer.jar`, replacing `hytale.com` with the custom auth domain. Use as a fallback if DualAuth causes issues.
- **Mode transition safety:** Switching between modes is handled gracefully — when switching from legacy to DualAuth, the server JAR is restored from its `.original` backup first; when switching from DualAuth to legacy, the JAR is re-patched.
- **DualAuth JWKS domain:** The auth domain for JWKS discovery is derived from the sessions domain by replacing `sessions.` prefix with `auth.` (e.g., `sessions.sanasol.ws` → `auth.sanasol.ws`). This is handled by `DeriveAuthDomain()` in GameLauncher.
- **AOT cache invalidation:** Before launch, `InvalidateAotCacheIfNeeded()` detects JVM flag changes via SHA-256 hash comparison and deletes `.aot` and `.jsa` cache files in the Server directory to prevent UseCompactObjectHeaders mismatches.
- **Stop control:** Game stop is available through IPC (`hyprism:game:stop`) and can be triggered from Dashboard and Instances actions.
- **Official 403 recovery:** If official CDN download returns HTTP 403 (expired signed `verify` token), the service force-refreshes version cache and retries official download once before mirror fallback.
- **Pre-release mirror fallback:** If target full mirror file is invalid/missing (for example 0-byte placeholder), service tries previous full build and applies patch chain to target version.
- **Selected instance launch sync:** selecting an instance updates both `SelectedInstanceId` and legacy launch fields (`VersionType`/`SelectedVersion`), and Dashboard launch sends explicit branch/version to avoid stale-target launches.
- **Instance-menu install mode:** Launch requests from Instances page can set `launchAfterDownload=false`, so download/install completes without auto-starting the game.
- **Launch path priority:** if `SelectedInstanceId` is set, `GameSessionService` resolves launch/install path by instance ID first (no fallback to another installed instance with same branch/version).
- **Latest metadata storage:** `latest.json` is stored under branch root (`Instances/<branch>/latest.json`) instead of `Instances/<branch>/latest/latest.json`, preventing accidental creation of placeholder `latest` instance folders.
- **Linux NVIDIA EGL fix:** in dedicated GPU mode, launcher exports `__EGL_VENDOR_LIBRARY_FILENAMES` to detected NVIDIA GLVND vendor JSON (e.g. `/usr/share/glvnd/egl_vendor.d/10_nvidia.json`) to avoid fallback to `llvmpipe` on affected systems.

### ClientPatcher ⚠️
- **File:** `Services/Game/ClientPatcher.cs`
- **CRITICAL:** Binary manipulation for game integrity
- **Rule:** NEVER modify without explicit instruction

### VersionService
- **Purpose:** Manages game version discovery and download URL resolution
- **Sources:** Queries official Hytale API and community mirrors (defined by JSON meta files in `Mirrors/`) for version lists
- **Unified caching:** Both version lists (`versions.json`) and patch chains (`patches.json`) are cached by VersionService from ALL sources (official + mirrors) in a single coordinated fetch. The two caches share the same multi-source structure (official data + per-mirror data keyed by source ID).
- **Cache sanitization:** Both `versions.json` and `patches.json` mirror lists are sanitized against currently registered mirror source IDs, removing stale/legacy entries and duplicate mirror IDs
- **Mirror fallback:** If official servers are unavailable, mirrors are auto-tested and selected at runtime, with failover across available mirrors when needed
- **Release download policy:** For mirror `release`, launcher prefers full standalone builds; patch metadata remains cached for future update flows

### IVersionSource
- **File:** `Services/Game/Sources/IVersionSource.cs`
- **Diagnostic layout info:** Each source exposes `LayoutInfo` with three explicit fields:
  - where full builds are downloaded (`FullBuildLocation`)
  - where diff patches are downloaded (`PatchLocation`)
  - how source-level cache works (`CachePolicy`)
- **Goal:** Reduce ambiguity in mirror patch/full behavior and speed up troubleshooting of wrong URL assumptions

### JsonMirrorSource
- **File:** `Services/Game/Sources/JsonMirrorSource.cs`
- **Purpose:** Universal mirror implementation driven by JSON meta descriptors (`Mirrors/*.mirror.json`)
- **Source types:** `pattern` (URL templates + version discovery) and `json-index` (single API endpoint)
- **Patch chain:** `GetPatchChainAsync` builds a full patch chain from known versions + URL templates, enabling VersionService to cache mirror patches alongside official data
- **Speed test:** Supports mirror speed testing with ping and download speed measurement

### HytaleVersionSource
- **File:** `Services/Game/Sources/HytaleVersionSource.cs`
- **Purpose:** Official authenticated version source (`account-data.hytale.com/patches`)
- **Patch chain:** `GetPatchChainAsync` fetches patch steps via `from_build=1` API call
- **Header compatibility:** Sends launcher-compatible headers (`User-Agent`, `x-hytale-launcher-version`, `x-hytale-launcher-branch`) and keeps token-refresh retries for 401/403

### ModService
- **Purpose:** Mod listing, searching, and management (CurseForge integration)
- **Instance mods source:** Reads from `UserData/Mods` and falls back to file-system discovery (`.jar`, `.zip`, `.disabled`) when manifest entries are missing
- **Download URL fallback:** if CurseForge returns no `downloadUrl` and `/download-url` is forbidden/empty, the service derives a deterministic CDN URL from `fileId + fileName`.
- **Version compatibility:** Mods with a specific `ServerVersion` in their JAR `manifest.json` (format: `YYYY.MM.DD-<hash>`) are automatically quarantined if the installed server's version doesn't match. Mods with `ServerVersion: "*"` (wildcard) are always compatible.

## User Services (`Services/User/`)

### ProfileService
- **Purpose:** Player profile CRUD operations
- **Features:** Multiple profiles, avatar management, profile switching
- **Mods storage policy:** profile switching does not redirect `UserData/Mods` to `Profiles/.../Mods`; mods remain instance-local.
- **Profile folder format:** profile folders are stored under `Profiles/{profileId}` (GUID).
- **Legacy migration:** launcher attempts to migrate legacy name-based profile folders in `Profiles/` to ID-based layout at startup (best-effort, non-destructive merge when both folders exist).
- **Official profile auth routing:** switching to an official profile automatically sets auth domain to `sessionserver.hytale.com`.
