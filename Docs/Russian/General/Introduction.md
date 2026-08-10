<!--
Copyright (C) 2026 HyPrism Launcher
SPDX-License-Identifier: GPL-3.0-only
-->

# Введение

**HyPrism** — это кроссплатформенный лаунчер игры Hytale, созданный с использованием современных технологий.

## Технологический стек

| Уровень | Технология |
|---------|-----------|
| Backend | .NET 10, C# 13 |
| Оболочка | Electron.NET (Electron 34) |
| Frontend | React 19 + TypeScript 5.9 + Vite 7 |
| Анимации | Framer Motion |
| Стилизация | TailwindCSS v4 |
| Иконки | Lucide React |
| Маршрутизация | React Router DOM |
| DI | Microsoft.Extensions.DependencyInjection |
| Логирование | Serilog |
| Локализация | i18next (12 языков) |

## Принцип работы

HyPrism запускается как **консольное приложение .NET**, которое создаёт процесс **Electron**. Окно Electron загружает React SPA из локальной файловой системы. Всё взаимодействие между React-фронтендом и .NET-бэкендом происходит через **IPC-каналы** (Inter-Process Communication — межпроцессное взаимодействие).

```
Консольное приложение .NET → создаёт процесс Electron
  ├── Electron Main Process
  │     └── BrowserWindow (без рамки, contextIsolation)
  │           └── preload.js (contextBridge → ipcRenderer)
  └── React SPA (загружается из file://wwwroot/index.html)
        └── ipc.ts → IPC-каналы → IpcService.cs → .NET сервисы
```

Это **НЕ** веб-сервер — здесь нет ASP.NET, HTTP или REST. Фронтенд взаимодействует с бэкендом исключительно через именованные IPC-каналы посредством сокетного моста Electron.

## Ключевые принципы

1. **Единый источник истины** — C#-аннотации в `IpcService.cs` определяют все IPC-каналы и TypeScript-типы; IPC-клиент для фронтенда полностью генерируется автоматически
2. **Изоляция контекста** — `contextIsolation: true`, `nodeIntegration: false`; все API Electron доступны через `preload.js`
3. **DI повсюду** — Все .NET-сервисы регистрируются в `Bootstrapper.cs` через внедрение через конструктор
4. **Кроссплатформенность** — Поддержка Windows, Linux, macOS благодаря .NET 10 + Electron
5. **Экземплярная модель** — Каждая установка игры изолирована в собственной папке на основе GUID

## Поддерживаемые платформы

- **Windows** 10/11 (x64)
- **Linux** (x64) — AppImage, Flatpak
- **macOS** (x64, arm64)

## Поддерживаемые языки

HyPrism поддерживает 12 языков с возможностью переключения во время работы:

| Код | Язык |
|-----|------|
| en-US | Английский |
| ru-RU | Русский |
| de-DE | Немецкий |
| es-ES | Испанский |
| fr-FR | Французский |
| ja-JP | Японский |
| ko-KR | Корейский |
| pt-BR | Португальский (Бразилия) |
| tr-TR | Турецкий |
| uk-UA | Украинский |
| zh-CN | Китайский (упрощённый) |
| be-BY | Белорусский |
