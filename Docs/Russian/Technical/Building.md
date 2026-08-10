<!--
Copyright (C) 2026 HyPrism Launcher
SPDX-License-Identifier: GPL-3.0-only
-->

# Сборка

## Предварительные требования

- **.NET 10 SDK**
- **Node.js 20+** (включает npm; требуется только для прежнего Electron-хоста)
- **Git**

## Разработка

### Нативная Avalonia-версия

Первый нативный вертикальный срез можно собрать, запустить и протестировать без Node.js:

```bash
dotnet build Sources/HyPrism.Desktop/HyPrism.Desktop.csproj
dotnet run --project Sources/HyPrism.Desktop/HyPrism.Desktop.csproj
dotnet test Tests/HyPrism.Desktop.Tests/HyPrism.Desktop.Tests.csproj
```

Окно открывается в размере 1280×800 (минимум 1024×700), Google Sans и JetBrains Mono встроены как ресурсы Avalonia,
а профиль, экземпляры, загрузка, прогресс и запуск игры используют существующие .NET-сервисы напрямую.

Material Symbols хранятся напрямую как weight-400 ресурсы `StreamGeometry` в
`Sources/HyPrism.Desktop/Assets/Icons/MaterialSymbols.axaml`. Сборка Avalonia использует
этот зафиксированный словарь и не требует Node.js или отдельного toolchain для иконок.

Метаданные лицензий оформляются по спецификации REUSE. Читаемые тексты хранятся в
`Licenses/`; поскольку REUSE требует стандартное имя в верхнем регистре, для локальной
проверки временно выполните `mv Licenses LICENSES`, затем `reuse lint` и
`mv LICENSES Licenses`. `.github/workflows/reuse.yml` делает это переименование
автоматически перед официальным action. Google Sans и JetBrains Mono отмечены как `OFL-1.1`.

Все файлы проекта, формат которых поддерживает комментарии, содержат явный заголовок
`Copyright (C) 2026 HyPrism Launcher` и SPDX-заголовок GPL-3.0-only. JSON,
шрифты, изображения, аудио и другие форматы без безопасных комментариев покрываются
аннотациями `REUSE.toml`. Проверить заголовки можно командой
`python3 Scripts/license_headers.py --check`, а механически добавить отсутствующие —
`python3 Scripts/license_headers.py --write`. CI запускает эту проверку перед `reuse lint`;
генератор IPC также добавляет корректный заголовок в результат, а зафиксированный словарь
Material Symbols сохраняет upstream-метаданные Apache-2.0.

### Сборка прежнего Electron-хоста (Backend + Frontend)

```bash
dotnet build
```

Эта единственная команда запускает весь конвейер MSBuild:

1. `NpmInstall` — выполняет `npm ci` в `Sources/HyPrism.Launcher/Frontend/`
2. `GenerateIpcTs` — генерирует `Sources/HyPrism.Launcher/Frontend/src/lib/ipc.ts` из C#-аннотаций
3. `BuildFrontend` — выполняет `npm run build` (TypeScript + Vite)
4. `CopyFrontendDist` — копирует `Sources/HyPrism.Launcher/Frontend/dist/` → `bin/.../wwwroot/`
5. Стандартная компиляция .NET

### Запуск прежнего Electron-хоста

```bash
dotnet run --project Sources/HyPrism.Launcher/HyPrism.Launcher.csproj
```

Запускает консольное приложение .NET → создаёт процесс Electron → открывает окно.

### Разработка только фронтенда

```bash
cd Sources/HyPrism.Launcher/Frontend
npm run dev    # Vite dev-сервер на localhost:5173
```

Полезно для итераций над UI без перезапуска всего приложения. Примечание: IPC-вызовы не будут работать в автономном режиме (нет моста Electron).

### Перегенерация IPC

```bash
dotnet run --project Sources/HyPrism.IpcGen/HyPrism.IpcGen.csproj -- --project "Sources/HyPrism.Launcher/HyPrism.Launcher.csproj" --output "Sources/HyPrism.Launcher/Frontend/src/lib/ipc.ts"
```

Или автоматически запускается при `dotnet build`, когда изменяется `IpcService.cs`.

## Продакшен-сборка

```bash
# Сборка фронтенда для продакшена
cd Sources/HyPrism.Launcher/Frontend && npm run build

# Публикация .NET
dotnet publish Sources/HyPrism.Launcher/HyPrism.Launcher.csproj -c Release
```

Результат публикации находится в `Sources/HyPrism.Launcher/bin/Release/net10.0/linux-x64/publish/` (или эквивалент для другой платформы) и включает папку `wwwroot/` со скомпилированным фронтендом.

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

**CI-сборка:** В GitHub Actions pipeline для Linux теперь запускается цель `flatpak` вместе с остальными, поэтому .flatpak автоматически создаётся и загружается в артефакты воркфлоу. В релизе этот файл прикрепляется наряду с AppImage, DEB, RPM и TAR.
Linux-иконки генерируются из `Sources/HyPrism.Launcher/Frontend/public/icon.png` в `Build/icons/` во время публикации.
Источник AppStream-метаданных остаётся `Sources/HyPrism.Launcher/Properties/linux/io.github.hyprismteam.HyPrism.metainfo.xml`.

Релизный CI (`.github/workflows/release.yml`) публикует Linux-артефакты только для `linux-x64`. Релизные сборки Linux `arm64` не поддерживаются.

### macOS

```bash
dotnet publish Sources/HyPrism.Launcher/HyPrism.Launcher.csproj -c Release -r osx-x64
# Или для Apple Silicon:
dotnet publish Sources/HyPrism.Launcher/HyPrism.Launcher.csproj -c Release -r osx-arm64
```

Смотрите `Sources/HyPrism.Launcher/Properties/macos/Info.plist` для специфичных метаданных macOS.

### Windows

```bash
dotnet publish Sources/HyPrism.Launcher/HyPrism.Launcher.csproj -c Release -r win-x64
```

## Цели MSBuild

| Цель | Триггер | Назначение |
|------|---------|------------|
| `NpmInstall` | Перед `GenerateIpcTs` | `npm ci --prefer-offline` |
| `GenerateIpcTs` | Перед `BuildFrontend` | `dotnet run --project ../HyPrism.IpcGen/HyPrism.IpcGen.csproj` |
| `BuildFrontend` | Перед `Build` | `npm run build` в Sources/HyPrism.Launcher/Frontend/ |
| `CopyFrontendDist` | После `Build` | Копирование dist → wwwroot |

Все цели используют инкрементальную сборку (Inputs/Outputs) для избежания лишней работы.
