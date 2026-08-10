<!--
Copyright (C) 2026 HyPrism Launcher
SPDX-License-Identifier: GPL-3.0-only
-->

# Configuration

HyPrism stores its configuration in `config.json` inside the data directory.

## Settings

Access settings through the **Settings** page (gear icon in sidebar).

### Native Settings page (Avalonia preview)

- The native page groups all currently supported launcher preferences into General, Downloads, Java, Visual, Network, Graphics, Variables, Data, About, and Developer categories.
- Like the News reader, wide windows use a dedicated `#18191B` navigation surface beside an independent content pane. This darker shared surface separates navigation without the brighter gray cast of the old translucent stroke color, and category titles remain white in every interaction state. Compact windows first show the full category list and slide horizontally to the selected section; the content view uses the same compact toolbar style as News, with **Back** on the left and the active category title centered instead of repeating a title and subtitle below it.
- Category rows fill the available content width of the navigation pane, so hover and selection feedback cover the complete row in both layouts. Every vertical page scroller now uses one application-wide style: a stable reserved gutter prevents content from sitting underneath the scrollbar, the track stays transparent, and a centered fully rounded thumb animates its actual width from 3 to 6 px without a scale transform or edge jump. Wide and compact rows both show a category-specific Fluent icon—including a dedicated information icon for About—and a concise localized summary of the whole section instead of reusing a hint from one individual option. Compact rows retain the same dark surface, white-title/secondary-description hierarchy, and subtle full-row hover transition as News cards, with no preselected highlight. Resizing an open wide section into compact mode keeps that section open instead of returning to the category list.
- Related options are arranged as dense setting rows inside a small number of grouped surfaces. Selectors stay aligned with their labels, while longer values and explicit-save editors use the full content width; this keeps both large and minimum-size windows useful without oversized cards.
- Switches and selectors are persisted immediately through `ISettingsService`. Selectors use the same fully opaque dark active surface for their strongly rounded menu, keep a small visual gap below the field, use a compact chevron, and fade both in and out. The language selector shows a country flag beside every native language name in both the field and menu. Changing the interface language reloads native labels and culture-sensitive dates without restarting the launcher. Text settings such as the authentication domain, custom Java path/JVM arguments, and game environment variables use an explicit **Save** action.
- The Avalonia visual theme keeps its fixed white accent; the Visual category exposes the background, music, and news visibility controls without reintroducing a conflicting accent picker.

### Native Dashboard (Avalonia preview)

- The native home page is a focused launch screen rather than a statistics dashboard. The selected instance, its branch/version and readiness state sit directly over a masked full-surface background.
- One white primary action changes between Select, Download, Play, Cancel and Stop. Installation progress is rendered inside the same launch area; activity notifications only fall back to the global overlay while another page is open.
- The compact instance button opens instance management. On wider content areas, up to three instances also appear in a flat quick-switch strip along the bottom. Selecting one updates the launch target immediately; the strip disappears below 900 px of dashboard width so the launch action retains enough space.
- Dashboard controls use explicit Avalonia templates without Fluent press scaling. Hover and selected states change only color and restrained surface opacity.

### General

| Setting | Description | Default |
|---------|-------------|---------|
| Language | UI language (13 available) | System language or en-US |
| Close after launch | Close launcher when game starts | false |
| Launch on startup | Auto-start with OS | false |
| Minimize to tray | Minimize to system tray | false |

### Appearance

| Setting | Description | Default |
|---------|-------------|---------|
| Animations | Enable UI animations | true |
| Transparency | Glass-morphism effects | true |
| Background mode | Dashboard background style | default |

Settings use borderless controls throughout the native UI. Switches show state through a gray or green track without separate On/Off labels; drop-downs and text fields use smooth hover feedback without focus outlines. Language flags are derived from `country-flag-icons` 1.6.20 under the MIT license.

### Native News page (Avalonia preview)

- Open **News** in the sidebar to see the newest official Hytale posts. GitHub releases are excluded.
- News uses the same dark-gray surface as the other native pages. Every fetched post, including the newest one, uses the same flat list row without a section heading, redundant source badge, edge arrow, or hover/selection outline. Rows gain only a subtle animated hover surface and a persistent selected surface; repeated clicks on the selected row are ignored. Short titles allow a two-line summary, while wrapped titles reserve more room for the headline. The feed column has its own slim scrollbar. **Load More** at the end requests the next page without adding a hover background; only its text color changes smoothly.
- Covers, summaries, authors, dates, and categories are read from the official Hytale page. The feed is stored for 30 minutes and parsed articles for seven days under `Cache/News`; in-memory article view models and already decoded images are reused during the process lifetime. The reader waits briefly before showing its skeleton, so memory and file-cache hits do not flash a loading state; the opaque article surface also prevents a retired skeleton frame from showing through ready content. Network, AngleSharp parsing, article-model construction, and high-quality bitmap decoding run away from the UI thread. Large formatting trees are attached to Avalonia in small dispatcher batches so a long patch-notes post cannot monopolize an animation frame. Pending feed-cover work is cancelled during the compact transition. Different article URLs can load concurrently and duplicate requests for the same URL are coalesced.
- On compact windows, selecting a row slides an opaque reader over the single-column feed with cubic easing and temporary edge-fade masks. The lightweight reader shell is prepared before the slide; heavy rich content and media start after it, while the outgoing article remains intact until the Back animation has finished. This prevents both an opening hitch on long posts and an empty-reader flash. After the slide, the text below the hero fades in as soon as its first batch is ready. A taller borderless toolbar above the cover uses balanced top/bottom spacing and always matches the cover width and inset. Its **Back** and **Link** actions remain fixed while the larger current title fades into the exact center after scrolling. Escape also returns to the list.
- On wide windows, the feed stays on a subtly tinted left surface and the selected article opens in the right column without a separate divider line or redundant **Back** button. The borderless **Link** action is part of the hero cover and sits immediately above the article title. Article changes use a single short fade after the new cover and its dark mask are ready, so the hero never flashes unmasked.
- The feed and reader use eased wheel scrolling without an immediate first-frame jump. Their slim scrollbars expose only the track and thumb, without line-step arrow buttons. Middle-click enables browser-style vertical auto-scroll: acceleration and deceleration are interpolated, the native cursor starts as four-way, changes to up/down for the current direction, and returns to four-way in the dead zone. Another middle click, a regular click, or Escape stops it. The reader also preserves headings, emphasis, compact quotes, nested ordered and unordered lists, collapsible technical sections, code and lazy-loaded media. Inline code is presented as a restrained translucent chip; full code blocks use a padded dark surface and subtle purple-gray border. Both use the bundled JetBrains Mono font. Collapsible headers use their full width for hover and click feedback, while trailing emotes remain centered beside the label. Headings use the font's natural line metrics so descenders remain visible. Paragraph-wrapped and plain list items use the same compact spacing. The cover metadata reads author, article type, and date; the site's generic SEO description is omitted from the article. Links inside an article are light purple without a permanent underline; they use a hand pointer, smoothly reveal the underline on hover, and open in the system browser. Blog emotes remain inline at their original relative sizes, while gallery images use full content width.
- The borderless window remains resizable from every edge and corner; the pointer indicates the active resize direction. Its compact minimize, maximize and close controls smoothly brighten on hover, with the minimize bar aligned to the lower part of its button.

### Game

| Setting | Description | Default |
|---------|-------------|---------|
| Resolution | Game window resolution | 1920x1080 |
| Sound | Game sound enabled | true |
| GPU preference | Graphics adapter selection | auto |

- **Optimization mods installer** now asks which instance should receive optimization mods before installation.

### Java

| Setting | Description | Default |
|---------|-------------|---------|
| Java runtime | Bundled Java or custom executable path | Bundled Java |
| Max RAM | Graphical slider for Java max heap (`-Xmx`) | 4096 MB |
| Initial RAM | Graphical slider for Java initial heap (`-Xms`) | 1024 MB |
| Garbage collector | Auto profile or explicit G1GC profile | Auto |
| Advanced JVM arguments | Optional extra JVM flags passed through JAVA_TOOL_OPTIONS (unsafe launch flags are filtered) | empty |

- When custom Java is enabled, use the **Select** button to pick an executable and save only after path validation.

#### GPU Preference Options

| Value | Description |
|-------|-------------|
| auto | Let the system choose the best GPU |
| dedicated | Force dedicated graphics (NVIDIA/AMD) |
| integrated | Force integrated graphics (Intel/AMD) |

### Advanced

| Setting | Description | Default |
|---------|-------------|---------|
| Developer mode | Show developer tools | false |
| Verbose logging | Extended log output | false |
| Data directory | Custom data storage path | Platform default |
| Download source | Managed automatically by launcher (official first, mirrors as fallback) | auto |
| Launch after download | Automatically start the game after install/download completes | true |

#### Download Source Strategy

- HyPrism always tries official Hytale sources first.
- If official download is unavailable, launcher automatically tests available mirrors and uses the best reachable one.
- Mirror choice is not persisted as a user setting.
- Mirrors are defined by JSON meta files in the `Mirrors/` folder (see [Custom Mirrors](#custom-mirrors) below).
- If no download sources are available, the Dashboard will show a **No Download Sources** warning when you click **Download** or **Play**.

## Custom Mirrors

HyPrism supports a data-driven mirror system. Mirrors are defined by `.mirror.json` files in the `Mirrors/` folder inside the launcher data directory. Default mirror definitions are auto-generated on first launch.

For full documentation on mirror configuration — including schema reference, all source types, version discovery methods, URL placeholders, annotated examples of all built-in mirrors, and step-by-step tutorials for creating your own — see the **[Mirrors Guide](Mirrors.md)**.

## Instance Management

Instead of a single game installation, HyPrism uses **instances** — isolated game installations in separate folders.

### Instance Structure

Each instance is stored in a version-based folder under its branch:

```
Instances/
└── release/
	├── v8/
	│   ├── game/           # Game files
	│   ├── mods/           # Installed mods
	│   └── meta.json       # Instance metadata (includes internal ID)
	├── latest/
	│   └── ...
	└── ...
```

### Managing Instances

- **Create** — Download a new game installation
- **Switch** — Select which instance to launch
- **Delete** — Remove an instance (confirmation required)
- **View details** — See version, patch status, installed mods
- **Dashboard instance shortcut** — Click the icon placeholder left of Play to open the Instances page focused on the current selected instance
- **Switcher layout behavior** — Instance switcher and main action button are centered together as a single control group
- **Dashboard icon fallback** — If a custom icon cannot be loaded, the switcher now falls back to the version badge instead of showing an empty icon slot
- **Centered play action** — The main Play button stays centered on the dashboard even when the instance switcher is visible
- **Per-instance icon fidelity** — Dashboard icon mapping is keyed per unique instance identity to prevent one custom icon from being shown on other entries
- **Full icon tiles** — Custom instance icons fill their switcher tiles for clearer visual identity
- **Startup icon detection** — Dashboard retries selected-instance icon loading during startup so custom icons appear without manually switching instances
- **Tighter dashboard spacing** — The Play row is positioned closer to the disclaimer badge

### Data Folder Quick Action

- In **Settings → Data**, the **Open Launcher Folder** button opens the launcher data directory in your file manager.

## Profiles

HyPrism supports multiple player profiles. Switch between profiles via the sidebar profile selector.
The native sidebar labels the selected identity as `Hytale Account` when its profile has
`IsOfficial: true`; all other selected profiles are shown as `Offline Account`.

### Profile Data

Each profile stores:
- **Nickname** — Display name in-game
- **UUID** — Unique player identifier
- **Official account status** — Hytale OAuth profiles are saved with `IsOfficial: true` in `Profiles/profiles.json`
- **Avatar** — Profile picture (optional)
- **Skin backup** — Saved skin data
- **Hytale session** — Official account tokens are stored in the profile folder as `hytale_session.json`

### Skin Backup

Profiles can back up your Hytale skin. Backups are stored in:

```
Profiles/
├── {ProfileUUID}/
│   ├── profile.json    # Profile metadata
│   ├── hytale_session.json  # Official account session, when present
│   └── skin.png        # Backed up skin
└── ...
```

Use the profile menu to:
- **Backup skin** — Save current skin to profile
- **Restore skin** — Apply backed up skin to account

## Mod Compatibility Safety

Before launch, HyPrism validates `UserData/Mods` for known-incompatible server mod metadata.

- Mods with a `ServerVersion` in the format `YYYY.MM.DD-<build>` are automatically moved to:
- This prevents Hytale's singleplayer server crash (`Invalid X-Range` / `Server failed to boot`).
- You can re-enable a moved mod manually by moving the `.jar` back to `UserData/Mods`.

## Installed Mods Selection Shortcuts

In both **Installed Mods** and **Browse Mods** tabs, HyPrism supports faster multi-select for mods:

- **Click** selects a single mod.
- **Ctrl/Cmd + Click** toggles a mod in the selection.
- **Shift + Click** replaces the current selection with the range from your anchor mod to the clicked mod.

When one or more mods are selected in **Installed Mods**, bulk actions (like **Enable Selected** / **Disable Selected**) apply to the entire selection.

## Drag-and-Drop Mod Import

- In **Installed Mods**, you can import mods by dragging files into the mods list.
- Supported drop formats: `.jar`, `.zip`, `.disabled`.
- Very large files and unsupported formats are skipped to prevent freezes.
- After importing, the mods list refreshes automatically.
- Selection does not persist when switching tabs or instances.

## Instances and Worlds Quick Actions

- In the instance list, **Right Click** opens the same instance actions menu as the 3-dots button (Edit, Open Folder, Open Mods Folder, Export, Delete).
- In the **Worlds** tab, world cards now expose hover actions for **Open Folder** and **Delete**.
- Instance content tabs now use localized labels for **Installed Mods** and **Browse Mods** across all supported UI languages.

## CurseForge Mod Page Shortcut

- Clicking a mod name in the mod lists/details opens that mod's CurseForge page in your default browser.

## Logs in Settings

- The launcher logs are available directly inside the **Settings** sidebar as a dedicated **Logs** tab.
- The Logs tab fills the settings content area and keeps its scroll region aligned to the panel border.
- Logs are no longer shown as a separate main navigation page.
- In embedded Settings mode, the Logs header matches other settings sections (text header, no icon).
- The logs output panel uses a slightly lighter background for improved readability.

## macOS Menu Bar

- On macOS, HyPrism provides launcher actions in the app menu bar (for example **Settings**, **Instances**, and **Quit**).

## Default Mods Folder

- The default managed mods directory is under instance user data:
	- `HyPrism/Instances/<branch>/<instance-guid>/UserData/Mods`
- This replaces legacy `Client/mods` for default mod storage and operations.
- Profile switching does not re-route this folder to `Profiles/.../Mods`; it stays inside the selected instance.

## Custom Auth Launch Behavior

For non-official profiles using custom auth domains, HyPrism launches in **online authenticated mode**.

- **DualAuth (default):** The client binary is patched and a runtime Java Agent (`dualauth-agent.jar`) is downloaded from GitHub and injected via `-javaagent:`. Before each launch the launcher checks for a newer agent version and updates automatically. This is the recommended approach for most users.
- **Legacy JAR patching (opt-in):** Enabled via the `Legacy Patching` toggle in General settings. Both the client binary and `Server/HytaleServer.jar` are statically patched to replace `hytale.com` with your custom auth domain. Use this as a fallback if DualAuth causes issues.
- Switching between modes is safe: the launcher automatically manages `.original` backup files when toggling legacy patching on/off.
- The auth domain is used as entered (for example `auth.example.com`); HyPrism no longer forces `sessions.` prefix.
- For compatibility, if direct host fails, HyPrism also tries `sessions.<your-domain>` automatically.
- Launch identity prefers auth-server profile name fields to reduce owner-name/token mismatch issues.
- Dashboard and Instances views both expose game stop controls while the game is running.

## Configuration File

**Location:**
- Windows: `%APPDATA%/HyPrism/config.json`
- Linux: `~/.local/share/HyPrism/config.json`
- macOS: `~/Library/Application Support/HyPrism/config.json`

The config file is JSON and can be edited manually, but it's recommended to use the Settings page.

### Data Directory

HyPrism uses a fixed launcher data directory based on your platform default.

- The path is shown in **Settings** → **Data**
- Launcher data directory relocation is not supported
- The launcher provides an **Open** button to open the containing folder
