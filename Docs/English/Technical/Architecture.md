<!--
Copyright (C) 2026 HyPrism Launcher
SPDX-License-Identifier: GPL-3.0-only
-->

# Architecture

## Avalonia migration status

HyPrism is transitioning from Electron.NET to a native Avalonia 12 desktop host.
During the transition both hosts remain available:

```
HyPrism.Core       shared .NET 10 models and services (no Electron dependency)
       ↑                              ↑
HyPrism.Desktop                 HyPrism.Launcher
Avalonia 12 + MVVM              Electron.NET + React + IPC
```

`HyPrism.Core` physically owns the launcher models, dependency-registration bootstrapper,
and the platform-neutral App, Infrastructure, Integration, Game, and User services.
`HyPrism.Launcher` references that assembly and contains only the legacy Electron host,
IPC transport/contracts, and Electron clipboard adapter. `HyPrism.Desktop` references the
same Core assembly directly, so shared logic is compiled exactly once.

The native shell uses compiled Avalonia bindings, `CommunityToolkit.Mvvm`, the
existing JSON locale files, and locally bundled Google Sans fonts. It currently
provides the application shell, real profile/instance data, launch/install action,
progress/game-state notifications, and a native news feed backed by `INewsService`.
The native Settings route is also service-backed; Instances, Mods, and Profiles remain migration placeholders.
Its visual foundation uses a fixed white accent and rounded Material Symbols stored
directly as weight-400 AXAML geometries. Navigation cross-fades between the
package's outline/filled paths without Fluent hover or press effects. Explicit native
move/resize zones support the custom window chrome and expose matching directional cursors.

The Avalonia Dashboard is isolated in `DashboardView` and keeps launch orchestration in
`MainWindowViewModel`. It presents the selected instance and a single stateful action over the
background instead of duplicating Instances, Mods, and News as metric cards. Download activity is
embedded in the launch surface, while a derived visibility state keeps the shared activity overlay
available on other routes. A width state hides the three-item quick switcher below 900 px; selecting
an item updates `IInstanceService` before rebuilding the presentation state.

The native News route requests only official Hytale posts from the shared service;
GitHub releases are not included in this page. The feed initially presents twelve posts as a uniform flat
vertical list and pages forward in groups of eight up to the service limit. Below 1180 px of content width,
an Avalonia `Carousel` and eased horizontal `PageSlide` transition between the feed and an opaque reader; temporary gradient masks soften content at both viewport edges. At 1180 px and above,
the page becomes a master/detail grid with a fixed 420 px tinted feed surface and a scrollable article pane.
The surface color itself separates the panes. Feed rows have animated hover/selected fills but no outline, permanent
card fill, section label, arrow, or Hytale source badge. The compact reader toolbar observes the article offset through
`SmoothScrollViewer`, always shares the hero's constrained width and horizontal inset, and fades in a larger, exactly
centered title after leaving the top without moving either action.
Wide readers omit Back and render the external action inside the hero above its title. Both actions remain transparent
and borderless in every pointer state.
Cover images load asynchronously, while the article text remains constrained for readability.
`NewsService` parses the server-rendered Hytale HTML with AngleSharp instead of matching markup
with regular expressions. The feed costs one request; a full article is fetched only on demand and converted into a sanitized formatting tree (paragraphs, headings,
links, images, inline images, quotes, collapsible details, hierarchical lists, emphasis and code). Native list view models
preserve nested ordered and unordered levels instead of flattening child lists into the parent row. Link
interaction is hit-tested against each rendered text range, including wrapped ranges. Block media wrapped by Hytale in
paragraph or formatting elements is normalized into first-class image blocks. Hytale `emote` and
`emote-sticker` classes remain inline nodes rendered at 1.5 em and 4 em respectively. A paragraph
that starts with a sticker is composed as a sticker-and-lead row followed by a full-width continuation,
matching the source blog without line overlap. Avalonia renders that tree without executing remote HTML
or JavaScript. Formatting-only whitespace around paragraph-wrapped list items is discarded so their
vertical rhythm matches ordinary list items. The article header combines author, post categories and date;
the site's generic SEO description is not rendered as article content. Sanitized `http` and `https` inline links and the explicit original-post action use the
platform browser service, so navigation opens in the operating system's default browser. `NewsRichTextBlock` realizes inline code as a compact rounded `InlineUIContainer` chip and preserves text-layout positions for adjacent link hit testing. Its child text uses an explicit compact line height instead of inheriting the article paragraph metric, preventing chips from enlarging or overlapping wrapped lines. Both that chip and block-code controls use the bundled JetBrains Mono face; block code has its own padded, rounded surface. Theme selectors hide the Fluent `PART_LineUpButton` and `PART_LineDownButton` template parts on both news scroll viewers while retaining thumb dragging, wheel input and auto-scroll.
AngleSharp parsing, article view-model construction, and bitmap decoding run outside the UI dispatcher; pending feed-cover work is cancelled while the compact transition starts. The feed is persisted for 30 minutes and article trees for seven days below `Cache/News`. Per-URL in-flight tasks coalesce duplicate opens without serializing different articles, while repeated selection of the active row is ignored. The reader delays its pulsing skeleton long enough for fast cache hits and makes the ready surface opaque, retains completed view models and decoded bitmaps, and adds rich content controls to the dispatcher in small batches rather than blocking one frame with the entire tree. Compact selection publishes the lightweight incoming reader state before changing the `Carousel` index; rich-block realization and image loading wait until the slide has finished, then yield a full compositor frame between small batches. Once the first text batch exists, only the body below the hero fades in; the toolbar and hero remain stable. Collapsed details expose no child item source until expanded, avoiding hidden control-tree construction. Back navigation keeps the outgoing model alive through the slide. Wide selection hides the old host without animating it, lets the new hero image and gradient mask render together, and only then performs a short fade-in; this avoids the previous double-fade and unmasked first frame. `SmoothScrollViewer` uses eased wheel targets and interpolates middle-click velocity rather than applying movement from pointer events; it selects native four-way, upward, or downward cursors from the current target direction.

The Avalonia Settings route mirrors the News master/detail structure: a narrow `StrokeBrush` section feed and independent content pane above its compact breakpoint, then a clipped cubic horizontal slide between the full section list and selected content below it. Compact rows expose localized descriptions but suppress the persisted section selection visually. Reader visibility is tracked separately, so crossing the breakpoint from an open wide pane preserves the content page. A section may contain several semantic categories. Every category owns one clipped background card containing full-width option rows; the rows use the application surface color as an edge-to-edge separator, and a compact heading sits directly above each card in both layouts. The compact toolbar names the selected section rather than replacing category headings. The compact reader returns through an explicit Back action.
It binds existing preference switches and selectors directly to `ISettingsService`; editable text fields
are persisted by explicit save commands. Language selection updates the Desktop-owned ResX `LocalizationService`, the current cultures,
top-level labels, settings data, account text, and news dates without recreating the window or the settings
view model. The existing selectors receive refreshed display strings in place, preventing binding-driven
selection changes from writing the language again. Core only persists the normalized language tag and has no locale catalog, resource lookup, culture, or notification responsibilities.
The Java pane reuses the legacy runtime-list layout but renders it entirely with Avalonia controls. Its linked
`-Xmx` and `-Xms` sliders write normalized 256 MB values through Core's `JvmArgumentBuilder`; Core owns heap-argument parsing, removal and replacement, while Desktop owns physical-memory limits, presentation and path validation. Launcher-managed heap flags are stripped from the user-facing advanced-argument editor and merged back only when settings are persisted. No garbage-collector profile is synthesized by this UI.
Settings selectors use `FadingComboBox`, which separates the logical open state from popup visibility so the popup stays rendered for the close transition. The language choices load the 3:2 country bitmaps bundled under `Assets/Flags`; only the 13 regions represented by the locale catalog are shipped.

`IGameLaunchCoordinator` is the first transport-neutral application entry point.
Both the Electron `hyprism:game:launch` channel and the Avalonia ViewModel call it,
so launch orchestration no longer lives inside the IPC adapter.

## Overview

HyPrism follows a **Console + IPC + React SPA** architecture pattern:

```
┌─────────────────────────────────────────────────────┐
│  .NET Console App  (Program.cs)                     │
│  ├── HyPrism.Core (models, DI, launcher services)   │
│  └── IpcService.cs (Electron IPC adapter)           │
│         ↕ Electron.NET socket bridge                │
│  ┌─────────────────────────────────────────────┐    │
│  │  Electron Main Process                      │    │
│  │  └── BrowserWindow (frameless)              │    │
│  │       └── preload.js (contextBridge)        │    │
│  │            ↕ ipcRenderer                    │    │
│  │       ┌─────────────────────────────┐       │    │
│  │       │  React SPA                  │       │    │
│  │       │  ├── App.tsx (routing)      │       │    │
│  │       │  ├── pages/ (views)         │       │    │
│  │       │  ├── components/ (shared)   │       │    │
│  │       │  └── lib/ipc.ts (generated) │       │    │
│  │       └─────────────────────────────┘       │    │
│  └─────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────┘
```

## Startup Flow

1. `Program.Main()` initializes Serilog logger
2. Installs `ElectronLogInterceptor` on `Console.Out`/`Console.Error`
3. `Bootstrapper.Initialize()` builds the DI container
4. `ElectronNetRuntime.RuntimeController.Start()` spawns Electron process
5. `ElectronBootstrap()` creates a frameless `BrowserWindow` loading `file://wwwroot/index.html`
6. `IpcService.RegisterAll()` registers all IPC channel handlers
7. React SPA mounts, fetches data via typed IPC calls

## Communication Model

All frontend ↔ backend communication uses **named IPC channels**:

```
Channel naming: hyprism:{domain}:{action}
Examples:       hyprism:game:launch
                hyprism:settings:get
                hyprism:i18n:set
```

### Channel Types

| Type | Direction | Pattern |
|------|-----------|---------|
| **send** | React → .NET (fire-and-forget) | `send(channel, data)` |
| **invoke** | React → .NET → React (request/reply) | `invoke(channel, data)` → waits for `:reply` |
| **event** | .NET → React (push) | `on(channel, callback)` |

### Security Model

- `contextIsolation: true` — renderer has no access to Node.js
- `nodeIntegration: false` — no `require()` in renderer
- `preload.js` exposes only `window.electron.ipcRenderer` via `contextBridge`

## IPC Socket Bridge

The IPC bridge uses HTTP socket for .NET ↔ Electron communication.

### VPN Compatibility (Windows)

On Windows, the socket binds to `0.0.0.0` instead of `127.0.0.1` to bypass VPN interception.

**Security**: All connections are filtered — only loopback addresses are accepted:
- `127.0.0.1` (IPv4 loopback)
- `::1` (IPv6 loopback)  
- `::ffff:127.0.0.1` (IPv6-mapped IPv4)

**Override**: Set `HYPRISM_VPN_COMPAT=0` to force `127.0.0.1` binding.

### Implementation

The socket bridge is patched in `.electron/custom_main.js` before Electron.NET initializes:

```javascript
// Windows defaults to 0.0.0.0, others use 127.0.0.1
const vpnCompatMode = vpnCompatEnv === '1' || (isWindows && vpnCompatEnv !== '0');

// Connection filtering
if (!isLoopback) {
    socket.destroy();  // Reject non-loopback
}
```

## Dependency Injection

Shared services are registered as singletons in `Sources/HyPrism.Core/Bootstrapper.cs`.
Each host may append its own adapters before the service provider is built:

```csharp
var provider = Bootstrapper.Initialize(services =>
{
    services.AddSingleton<ClipboardService>();
    services.AddSingleton<IpcService>();
});
```

The Electron host supplies `IpcService` and its clipboard implementation; neither is a dependency of Core.
`IpcService` resolves shared contracts such as `IModService`, rather than reaching into internal Core implementations.

## Log Interception

Electron.NET emits unstructured messages to stdout/stderr (e.g. `[StartCore]:`, `|| ...`). HyPrism intercepts these via `ElectronLogInterceptor` (a custom `TextWriter` installed on `Console.Out`/`Console.Error`) and routes them through the structured `Logger`:

- Framework messages → `Logger.Info("Electron", ...)`
- Debug messages (`[StartCore]`, `BridgeConnector`) → `Logger.Debug("Electron", ...)`
- Error patterns (`ERROR:`, `crash`) → `Logger.Warning("Electron", ...)`
- Noise patterns (`GetVSyncParametersIfAvailable`) → suppressed
