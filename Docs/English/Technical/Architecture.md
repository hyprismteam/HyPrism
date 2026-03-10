# Architecture

## Overview

HyPrism follows a **Console + IPC + React SPA** architecture pattern:

```
┌─────────────────────────────────────────────────────┐
│  .NET Console App  (Program.cs)                     │
│  ├── Bootstrapper.cs (DI container)                 │
│  ├── Services/ (business logic)                     │
│  └── IpcService.cs (IPC channel registry)           │
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

All services are registered as singletons in `Bootstrapper.cs`:

```csharp
var services = new ServiceCollection();
services.AddSingleton<ConfigService>();
services.AddSingleton<IpcService>();
// ... etc
return services.BuildServiceProvider();
```

`IpcService` receives all other services through constructor injection and acts as the central bridge between React and .NET.

## Log Interception

Electron.NET emits unstructured messages to stdout/stderr (e.g. `[StartCore]:`, `|| ...`). HyPrism intercepts these via `ElectronLogInterceptor` (a custom `TextWriter` installed on `Console.Out`/`Console.Error`) and routes them through the structured `Logger`:

- Framework messages → `Logger.Info("Electron", ...)`
- Debug messages (`[StartCore]`, `BridgeConnector`) → `Logger.Debug("Electron", ...)`
- Error patterns (`ERROR:`, `crash`) → `Logger.Warning("Electron", ...)`
- Noise patterns (`GetVSyncParametersIfAvailable`) → suppressed
