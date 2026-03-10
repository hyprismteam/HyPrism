# Сборка

## Предварительные требования

- **.NET 10 SDK**
- **Node.js 20+** (включает npm)
- **Git**

## Разработка

### Полная сборка (Backend + Frontend)

```bash
dotnet build
```

Эта единственная команда запускает весь конвейер MSBuild:

1. `NpmInstall` — выполняет `npm ci` в `Frontend/`
2. `GenerateIpcTs` — генерирует `Frontend/src/lib/ipc.ts` из C#-аннотаций
3. `BuildFrontend` — выполняет `npm run build` (TypeScript + Vite)
4. `CopyFrontendDist` — копирует `Frontend/dist/` → `bin/.../wwwroot/`
5. Стандартная компиляция .NET

### Запуск

```bash
dotnet run
```

Запускает консольное приложение .NET → создаёт процесс Electron → открывает окно.

### Разработка только фронтенда

```bash
cd Frontend
npm run dev    # Vite dev-сервер на localhost:5173
```

Полезно для итераций над UI без перезапуска всего приложения. Примечание: IPC-вызовы не будут работать в автономном режиме (нет моста Electron).

### Перегенерация IPC

```bash
node Scripts/generate-ipc.mjs
```

Или автоматически запускается при `dotnet build`, когда изменяется `IpcService.cs`.

## Продакшен-сборка

```bash
# Сборка фронтенда для продакшена
cd Frontend && npm run build

# Публикация .NET
dotnet publish -c Release
```

Результат публикации находится в `bin/Release/net10.0/linux-x64/publish/` (или эквивалент для другой платформы) и включает папку `wwwroot/` со скомпилированным фронтендом.

## Особенности платформ

### Linux

```bash
# Стандартная сборка
dotnet build

# Продакшен-публикация
dotnet publish -c Release -r linux-x64

# Flatpak-бандл (рекомендуется)
./Scripts/publish.sh flatpak --arch x64
```

Упаковка Flatpak теперь генерируется через `Scripts/publish.sh` и Electron Builder.
Linux-иконки генерируются из `Frontend/public/icon.png` в `Build/icons/` во время публикации.
Источник AppStream-метаданных остаётся `Properties/linux/io.github.hyprismteam.HyPrism.metainfo.xml`.

Релизный CI (`.github/workflows/release.yml`) публикует Linux-артефакты только для `linux-x64`. Релизные сборки Linux `arm64` не поддерживаются.

### macOS

```bash
dotnet publish -c Release -r osx-x64
# Или для Apple Silicon:
dotnet publish -c Release -r osx-arm64
```

Смотрите `Properties/macos/Info.plist` для специфичных метаданных macOS.

### Windows

```bash
dotnet publish -c Release -r win-x64
```

## Цели MSBuild

| Цель | Триггер | Назначение |
|------|---------|------------|
| `NpmInstall` | Перед `GenerateIpcTs` | `npm ci --prefer-offline` |
| `GenerateIpcTs` | Перед `BuildFrontend` | `node Scripts/generate-ipc.mjs` |
| `BuildFrontend` | Перед `Build` | `npm run build` в Frontend/ |
| `CopyFrontendDist` | После `Build` | Копирование dist → wwwroot |

Все цели используют инкрементальную сборку (Inputs/Outputs) для избежания лишней работы.
