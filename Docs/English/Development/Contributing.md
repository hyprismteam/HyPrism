# Contributing

## Getting Started

1. Fork the repository
2. Clone your fork
3. Install prerequisites: .NET 10 SDK, Node.js 20+
4. Run `dotnet build` to build everything (including frontend)
5. Run `dotnet run` to launch

## Development Workflow

1. Create a feature branch from `main`
2. Make changes following [Coding Standards](CodingStandards.md)
3. Test: `dotnet build` must complete with 0 errors
4. For frontend changes, also verify `cd Frontend && npx tsc --noEmit`
5. Commit with clear messages
6. Open a Pull Request

### Linux packaging icon note

- `Build/` is generated during packaging; source icon is `Frontend/public/icon.png`.
- For Linux packages (including Flatpak), `Scripts/publish.sh` generates `Build/icons/` with hicolor app-id icons (`io.github.hyprismteam.HyPrism`) to ensure icon export works after install.
- Linux package app ID is `io.github.hyprismteam.HyPrism`.
- AppStream metadata is injected for Linux packaging from `Properties/linux/io.github.hyprismteam.HyPrism.metainfo.xml`.
- RPM repack step intentionally strips `/usr/lib/.build-id` payload and does not own system directories (`/`, `/usr`, `/usr/lib`) to avoid install conflicts on Fedora.
- Flatpak packaging uses runtime/base `25.08`; CI prepares Flathub remotes at system and user levels so flatpak-builder can install deps, then installs `org.freedesktop.Platform`, `org.freedesktop.Sdk`, and `org.electronjs.Electron2.BaseApp` at system level before build.
- Linux CI prints Flatpak remotes/runtimes diagnostics to simplify troubleshooting when flatpak-bundler fails.

## Adding a New Feature

### Checklist

1. Add/update .NET service in `Services/{Core|Game|User}/`
2. Register in `Bootstrapper.cs` if new service
3. Add IPC handler + `@ipc` annotation in `IpcService.cs`
4. Add `@type` annotation if new TypeScript type is needed
5. Regenerate: `node Scripts/generate-ipc.mjs` (or `dotnet build`)
6. Create React component/page in `Frontend/src/`
7. Add route in `App.tsx` if new page
8. Update documentation in `Docs/`
9. Verify: `dotnet build` passes with 0 errors

### Adding an IPC Channel

See [IPC Code Generation](../Technical/IpcCodegen.md) for the full guide.

### Adding a Built-in Mirror

See [Adding a Mirror](AddingMirror.md) for the full guide on adding a community mirror to the default set.

## Critical Files

| File | Impact | Rule |
|------|--------|------|
| `ClientPatcher.cs` | Game integrity | Never modify without explicit instruction |
| `Program.cs` | App entry point | Changes affect entire startup |
| `Bootstrapper.cs` | DI setup | Breaking changes affect all services |
| `IpcService.cs` | IPC bridge | Must stay in sync with frontend |
| `preload.js` | Security boundary | Minimal changes only |

## Documentation

Every change must include documentation updates:
- **User docs** — when UI or feature behavior changes
- **Developer docs** — when build/CI/workflows change
- **API docs** — when IPC channels are added, renamed, or removed

Both English and Russian docs should be updated.

## Code Review Guidelines

- Follows coding standards (naming, braces, async suffix)
- No hardcoded values — use config, theme tokens, localization keys
- IPC changes update both C# annotations and verify generated output
- No references to deprecated `UI/` directory
- No manual edits to `Frontend/src/lib/ipc.ts`
