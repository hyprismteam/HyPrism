<!--
Copyright (C) 2026 HyPrism Launcher
SPDX-License-Identifier: GPL-3.0-only
-->

# Introduction

**HyPrism** is a cross-platform Hytale game launcher built with modern technologies.

## Technology Stack

| Layer | Technology |
|-------|-----------|
| Backend | .NET 10, C# 13 |
| Desktop Shell | Electron.NET (Electron 34) |
| Frontend | React 19 + TypeScript 5.9 + Vite 7 |
| Animations | Framer Motion |
| Styling | TailwindCSS v4 |
| Icons | Lucide React |
| Routing | React Router DOM |
| DI | Microsoft.Extensions.DependencyInjection |
| Logging | Serilog |
| Localization | .NET ResX (13 languages) |

## How It Works

HyPrism runs as a **.NET Console Application** that spawns an **Electron** process. The Electron window loads a React SPA from the local filesystem. All communication between the React frontend and .NET backend happens through **IPC channels** (Inter-Process Communication).

```
.NET Console App → spawns Electron process
  ├── Electron Main Process
  │     └── BrowserWindow (frameless, contextIsolation)
  │           └── preload.js (contextBridge → ipcRenderer)
  └── React SPA (loaded from file://wwwroot/index.html)
        └── ipc.ts → IPC channels → IpcService.cs → .NET Services
```

This is **NOT** a web server — there is no ASP.NET, no HTTP, no REST. The frontend communicates with the backend entirely through named IPC channels over Electron's socket bridge.

## Key Principles

1. **Single source of truth** — C# annotations in `IpcService.cs` define all IPC channels and TypeScript types; the frontend IPC client is 100% auto-generated
2. **Context isolation** — `contextIsolation: true`, `nodeIntegration: false`; all Electron APIs exposed via `preload.js`
3. **DI everywhere** — All .NET services registered in `Bootstrapper.cs` via constructor injection
4. **Cross-platform** — Windows, Linux, macOS support via .NET 10 + Electron
5. **Instance-based** — Each game installation is isolated in its own GUID-based folder

## Supported Platforms

- **Windows** 10/11 (x64)
- **Linux** (x64) — AppImage, Flatpak
- **macOS** (x64, arm64)

## Supported Languages

HyPrism supports 13 languages with runtime switching:

| Code | Language |
|------|----------|
| en-US | English |
| ru-RU | Russian |
| de-DE | German |
| es-ES | Spanish |
| fr-FR | French |
| it-IT | Italian |
| ja-JP | Japanese |
| ko-KR | Korean |
| pt-BR | Portuguese (Brazil) |
| tr-TR | Turkish |
| uk-UA | Ukrainian |
| zh-CN | Chinese (Simplified) |
| be-BY | Belarusian |
