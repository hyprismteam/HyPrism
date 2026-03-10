# Генерация IPC-кода

IPC-мост между рендерером Electron (React/TypeScript) и бэкендом .NET в HyPrism
**полностью типобезопасен и генерируется автоматически**. Разработчик пишет типизированные
C#-методы; Roslyn CLI-инструмент `HyPrism.IpcGen` читает их во время сборки и создаёт
`Frontend/src/lib/ipc.ts`.

---

## Архитектура

```
IpcService.cs                    HyPrism.IpcGen/          Frontend/src/lib/
   [IpcInvoke("channel")]  ──►  Анализ Roslyn       ──►  ipc.ts
   [IpcSend("channel")]         Маппинг типов              ├ export interface Foo { … }
   [IpcEvent("channel")]        Генерация TS               ├ export const ipc = { … }
                                                           └ export function invoke<T>(…)
```

Во время выполнения `IpcServiceBase.RegisterAll()` обнаруживает все методы с атрибутами
через рефлексию и регистрирует их в `Electron.IpcMain` автоматически — ручной вызов
`Electron.IpcMain.On(...)` не нужен.

---

## Атрибуты

### `[IpcInvoke("channel")]` — Запрос / Ответ

Рендерер вызывает метод и ждёт ответа.
Тип возвращаемого значения C# (или тип `T` из `Task<T>`) становится TypeScript-типом ответа.
Опциональный первый параметр становится типом входных данных.

```csharp
// Без входных данных, типизированный ответ
[IpcInvoke("hyprism:settings:get")]
public SettingsSnapshot GetSettings() { … }

// Типизированный вход + типизированный ответ
[IpcInvoke("hyprism:instance:create")]
public async Task<InstanceInfo?> CreateInstance(CreateInstanceRequest req) { … }

// Кастомный таймаут (мс, по умолчанию 10 000)
[IpcInvoke("hyprism:update:install", 300_000)]
public async Task<bool> InstallUpdate() { … }
```

Сгенерированный TypeScript:
```typescript
ipc.settings.get()          // Promise<SettingsSnapshot>
ipc.instance.create(data)   // Promise<InstanceInfo | null>
ipc.update.install()        // Promise<boolean>
```

---

### `[IpcSend("channel")]` — Без ответа (fire-and-forget)

Рендерер отправляет сообщение; ответ не ожидается.
Возвращаемый тип должен быть `void`; опциональный первый параметр — тип входных данных.

```csharp
[IpcSend("hyprism:game:launch")]
public void LaunchGame(LaunchGameRequest? req) { … }

[IpcSend("hyprism:window:minimize")]
public void MinimizeWindow() { … }
```

Сгенерированный TypeScript:
```typescript
ipc.game.launch(data)     // void
ipc.windowCtl.minimize()  // void
```

---

### `[IpcEvent("channel")]` — Push-уведомление (C# → JS)

Метод вызывается **один раз при старте** с делегатом `Action<T> emit`.
Реализация подписывается на C#-событие и вызывает `emit(data)` для отправки данных рендереру.

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

Сгенерированный TypeScript:
```typescript
ipc.game.onProgress((data: ProgressUpdate) => { … })
```

---

## Маппинг типов

Инструмент (`HyPrism.IpcGen/CSharpTypeMapper.cs`) преобразует C#-типы в TypeScript:

| C#-тип | TypeScript |
|--------|------------|
| `bool` | `boolean` |
| `string`, `char` | `string` |
| `int`, `long`, `double`, `float`, … | `number` |
| `T?` (nullable ref или value) | `T \| null` |
| `T[]`, `List<T>`, `IEnumerable<T>`, … | `T[]` |
| `Dictionary<K,V>`, `IReadOnlyDictionary<K,V>` | `Record<K, V>` |
| `Task<T>` | разворачивается до `T` |
| `Task` | `void` |
| Enum | `'ЧленA' \| 'ЧленB' \| …` |
| Именованный класс или record | `export interface Name { … }` |
| `DateTime`, `DateTimeOffset`, `TimeSpan` | `string` |
| `object` | `unknown` |

Все именованные C#-классы и records рекурсивно преобразуются в TypeScript-интерфейсы
с именами свойств в camelCase.

---

## Request- и Response-записи

Входные типы находятся в `Services/Core/Ipc/Requests/` (по одному файлу на домен);
ответные типы — в `Services/Core/Ipc/Responses/`.

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
├── IpcService.cs        ← все обработчики каналов
└── IpcServiceBase.cs    ← автоматическая регистрация через рефлексию
```

---

## Интеграция с MSBuild

`HyPrism.csproj` запускает `HyPrism.IpcGen` автоматически перед сборкой Vite-фронтенда:

```xml
<Target Name="GenerateIpcTs" BeforeTargets="BuildFrontend" DependsOnTargets="NpmInstall"
        Condition="Exists('HyPrism.IpcGen/HyPrism.IpcGen.csproj')">
  <Exec Command="dotnet run --project HyPrism.IpcGen/HyPrism.IpcGen.csproj
                 -- --project &quot;$(MSBuildProjectFullPath)&quot;
                    --output &quot;$(MSBuildProjectDirectory)/Frontend/src/lib/ipc.ts&quot;" />
</Target>
```

Инструмент использует SHA-256 хэш `IpcService.cs`, сохранённый в `Frontend/src/lib/.ipcgen.hash`.
Если файл не изменился с последнего запуска, генерация пропускается.

---

## Добавление нового канала

1. **Определите типы** (при необходимости) — добавьте record в `Requests/` или `Responses/`:
   ```csharp
   // Requests/GameRequests.cs
   public record MyActionRequest(string Param, int Count);

   // Responses/GameResponses.cs
   public record MyActionResult(bool Success, string? Message = null);
   ```

2. **Добавьте обработчик** в `IpcService.cs` внутри нужного `#region`:
   ```csharp
   [IpcInvoke("hyprism:game:myAction")]
   public async Task<MyActionResult> MyAction(MyActionRequest req)
   {
       var ok = await Services.GetRequiredService<IGameService>().DoSomethingAsync(req.Param);
       return new MyActionResult(ok);
   }
   ```

3. **Пересоберите проект** — `dotnet build` автоматически регенерирует `ipc.ts`.

4. **Используйте в React**:
   ```typescript
   import { ipc, type MyActionResult } from '@/lib/ipc';

   const result = await ipc.game.myAction({ param: 'hello', count: 3 });
   ```

---

## Конфликты имён доменов

Домены с именами `window` и `console` переименовываются во избежание конфликтов
с глобальными объектами JavaScript:

| IPC-домен | Ключ в `ipc` |
|-----------|-------------|
| `window`  | `ipc.windowCtl` |
| `console` | `ipc.consoleCtl` |

---

## Roslyn-инструмент — `HyPrism.IpcGen/`

| Файл | Ответственность |
|------|----------------|
| `Program.cs` | Точка входа CLI; хэш-кэш; настройка MSBuildWorkspace |
| `IpcMethodCollector.cs` | Поиск подклассов `IpcServiceBase`; извлечение дескрипторов `IpcMethod` |
| `CSharpTypeMapper.cs` | Рекурсивный маппинг `ITypeSymbol` → TypeScript-строки |
| `TypeScriptEmitter.cs` | Рендер итогового `ipc.ts` из собранных данных |
| `Models.cs` | Внутренние DTO: `IpcMethod`, `TsInterface`, `TsField`, `IpcKind` |
