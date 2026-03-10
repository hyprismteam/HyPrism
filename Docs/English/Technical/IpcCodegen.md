# IPC Code Generation

HyPrism's IPC bridge between the Electron renderer (React/TypeScript) and the .NET backend is
**fully type-safe and 100% auto-generated**. Developers write typed C# methods; the Roslyn CLI
tool `HyPrism.IpcGen` reads them at build time and produces `Frontend/src/lib/ipc.ts`.

---

## Architecture Overview

```
IpcService.cs                    HyPrism.IpcGen/          Frontend/src/lib/
   [IpcInvoke("channel")]  ──►  Roslyn analysis     ──►  ipc.ts
   [IpcSend("channel")]         Type mapping               ├ export interface Foo { … }
   [IpcEvent("channel")]        TS emission                ├ export const ipc = { … }
                                                           └ export function invoke<T>(…)
```

At runtime, `IpcServiceBase.RegisterAll()` discovers all attributed methods via reflection
and registers them on `Electron.IpcMain` automatically — no manual wiring needed.

---

## Attribute Reference

### `[IpcInvoke("channel")]` — Request / Reply

The renderer calls the method and awaits a reply.
The C# return type (or `Task<T>` inner type) becomes the TypeScript response type.
An optional first parameter becomes the TypeScript input type.

```csharp
// No input, typed response
[IpcInvoke("hyprism:settings:get")]
public SettingsSnapshot GetSettings() { … }

// Typed input + typed response
[IpcInvoke("hyprism:instance:create")]
public async Task<InstanceInfo?> CreateInstance(CreateInstanceRequest req) { … }

// Custom timeout (ms, default 10 000)
[IpcInvoke("hyprism:update:install", 300_000)]
public async Task<bool> InstallUpdate() { … }
```

Generated TypeScript:
```typescript
ipc.settings.get()                    // Promise<SettingsSnapshot>
ipc.instance.create(data)             // Promise<InstanceInfo | null>
ipc.update.install()                  // Promise<boolean>
```

---

### `[IpcSend("channel")]` — Fire-and-Forget

The renderer sends a message; no reply is expected.
Return type must be `void`; an optional first parameter becomes the input type.

```csharp
[IpcSend("hyprism:game:launch")]
public void LaunchGame(LaunchGameRequest? req) { … }

[IpcSend("hyprism:window:minimize")]
public void MinimizeWindow() { … }
```

Generated TypeScript:
```typescript
ipc.game.launch(data)     // void
ipc.windowCtl.minimize()  // void
```

---

### `[IpcEvent("channel")]` — Push Event (C# → JS)

The method is called **once at startup** with an `Action<T> emit` delegate.
The implementation subscribes to a C# event and calls `emit(data)` to push to the renderer.

```csharp
[IpcEvent("hyprism:game:progress")]
public void SubscribeGameProgress(Action<ProgressUpdate> emit)
{
    Services.GetRequiredService<ProgressNotificationService>()
        .DownloadProgressChanged += msg =>
        {
            try { emit(new ProgressUpdate(…)); } catch { }
        };
}
```

Generated TypeScript:
```typescript
ipc.game.onProgress((data: ProgressUpdate) => { … })
```

---

## Type Mapping

The Roslyn tool (`HyPrism.IpcGen/CSharpTypeMapper.cs`) maps C# types to TypeScript as follows:

| C# type | TypeScript |
|---------|------------|
| `bool` | `boolean` |
| `string`, `char` | `string` |
| `int`, `long`, `double`, `float`, … | `number` |
| `T?` (nullable ref or value) | `T \| null` |
| `T[]`, `List<T>`, `IEnumerable<T>`, … | `T[]` |
| `Dictionary<K,V>`, `IReadOnlyDictionary<K,V>` | `Record<K, V>` |
| `Task<T>` | unwrapped to `T` |
| `Task` | `void` |
| Enum | `'MemberA' \| 'MemberB' \| …` |
| Named class or record | `export interface Name { … }` |
| `DateTime`, `DateTimeOffset`, `TimeSpan` | `string` |
| `object` | `unknown` |

All named C# classes and records are recursively emitted as TypeScript interfaces
with camelCase property names.

---

## Request and Response Records

Input types live in `Services/Core/Ipc/Requests/` (one file per domain);
response types live in `Services/Core/Ipc/Responses/`.

```
Services/Core/Ipc/
├── Attributes/
│   ├── IpcInvokeAttribute.cs
│   ├── IpcSendAttribute.cs
│   └── IpcEventAttribute.cs
├── Requests/
│   ├── GameRequests.cs
│   ├── InstanceRequests.cs
│   ├── ModRequests.cs
│   ├── ProfileRequests.cs
│   └── SettingsRequests.cs
├── Responses/
│   ├── AuthResponses.cs
│   ├── CommonResponses.cs
│   ├── GameResponses.cs
│   ├── InstanceResponses.cs
│   ├── ProfileResponses.cs
│   ├── SettingsResponses.cs
│   ├── SystemResponses.cs
│   └── UpdateResponses.cs
├── IpcService.cs        ← all channel handlers
└── IpcServiceBase.cs    ← reflection-based auto-registration
```

---

## MSBuild Integration

`HyPrism.csproj` runs `HyPrism.IpcGen` automatically before the Vite frontend build:

```xml
<Target Name="GenerateIpcTs" BeforeTargets="BuildFrontend" DependsOnTargets="NpmInstall"
        Condition="Exists('HyPrism.IpcGen/HyPrism.IpcGen.csproj')">
  <Exec Command="dotnet run --project HyPrism.IpcGen/HyPrism.IpcGen.csproj
                 -- --project &quot;$(MSBuildProjectFullPath)&quot;
                    --output &quot;$(MSBuildProjectDirectory)/Frontend/src/lib/ipc.ts&quot;" />
</Target>
```

The tool uses a SHA-256 hash of `IpcService.cs` stored in `Frontend/src/lib/.ipcgen.hash`.
If the file has not changed since the last run, codegen is skipped entirely.

---

## Adding a New IPC Channel

1. **Define types** (if needed) — add a record in `Requests/` or `Responses/`:
   ```csharp
   // Requests/GameRequests.cs
   public record MyActionRequest(string Param, int Count);

   // Responses/GameResponses.cs
   public record MyActionResult(bool Success, string? Message = null);
   ```

2. **Add a handler** in `IpcService.cs` inside the appropriate `#region`:
   ```csharp
   [IpcInvoke("hyprism:game:myAction")]
   public async Task<MyActionResult> MyAction(MyActionRequest req)
   {
       var ok = await Services.GetRequiredService<IGameService>().DoSomethingAsync(req.Param);
       return new MyActionResult(ok);
   }
   ```

3. **Rebuild** — `dotnet build` regenerates `ipc.ts` automatically.

4. **Use in React**:
   ```typescript
   import { ipc, type MyActionResult } from '@/lib/ipc';

   const result = await ipc.game.myAction({ param: 'hello', count: 3 });
   ```

---

## Domain Name Conflicts

Domains named `window` or `console` are aliased to avoid shadowing JavaScript globals:

| IPC Domain | `ipc` Export Key |
|-----------|-----------------|
| `window`  | `ipc.windowCtl` |
| `console` | `ipc.consoleCtl` |

---

## Roslyn Tool — `HyPrism.IpcGen/`

| File | Responsibility |
|------|---------------|
| `Program.cs` | CLI entry point; hash cache; MSBuildWorkspace setup |
| `IpcMethodCollector.cs` | Finds `IpcServiceBase` subclasses; extracts `IpcMethod` descriptors |
| `CSharpTypeMapper.cs` | Recursively maps `ITypeSymbol` → TypeScript type strings |
| `TypeScriptEmitter.cs` | Renders the final `ipc.ts` from collected data |
| `Models.cs` | Internal DTOs: `IpcMethod`, `TsInterface`, `TsField`, `IpcKind` |
