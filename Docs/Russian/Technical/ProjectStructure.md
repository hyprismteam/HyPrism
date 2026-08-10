<!--
Copyright (C) 2026 HyPrism Launcher
SPDX-License-Identifier: GPL-3.0-only
-->

# Структура проекта

```
HyPrism/
├── HyPrism.sln                 # Файл решения (содержит папку Sources/)
│
├── Sources/                    # Проекты с production-кодом
    ├── HyPrism.Launcher/       # Главное приложение лаунчера (.NET 10 + Electron.NET)
    │   ├── Program.cs          # Точка входа: Console → Electron bootstrap
    │   ├── HyPrism.Launcher.csproj  # Файл проекта с конвейером MSBuild
    │   │
    │   ├── Frontend/           # React SPA (Vite + TypeScript)
    │   │   ├── src/
    │   │   │   ├── components/     # Переиспользуемые React-компоненты
    │   │   │   │   ├── layout/    # Компоненты компоновки (DockMenu, MusicPlayer и др.)
    │   │   │   │   ├── modals/    # Модальные диалоги
    │   │   │   │   ├── ui/        # UI-примитивы и элементы управления
    │   │   │   │   │   └── controls/  # Button, Switch, ScrollArea и др.
    │   │   │   │   ├── icons/     # Кастомные иконки
    │   │   │   │   └── dev/       # Инструменты разработчика
    │   │   │   ├── pages/          # Компоненты страниц маршрутизации
    │   │   │   │   ├── instances/  # Управление экземплярами (с вкладками)
    │   │   │   │   ├── settings/   # Страница настроек (с отдельными вкладками)
    │   │   │   │   │   └── tabs/   # Отдельные компоненты вкладок настроек
    │   │   │   │   ├── onboarding/ # Мастер первого запуска
    │   │   │   │   │   └── steps/  # Отдельные шаги онбординга
    │   │   │   │   ├── DashboardPage.tsx  # Главная панель (запуск игры)
    │   │   │   │   ├── NewsPage.tsx       # Страница ленты новостей
    │   │   │   │   ├── InstancesPage.tsx  # Менеджер экземпляров игры
    │   │   │   │   ├── ProfilesPage.tsx    # Управление профилями
    │   │   │   │   └── LogsPage.tsx        # Просмотр логов
    │   │   │   ├── contexts/       # Провайдеры React Context
    │   │   │   │   └── AccentColorContext.tsx  # Акцентный цвет темы
    │   │   │   ├── lib/            # Утилиты
    │   │   │   │   └── ipc.ts      # АВТОГЕНЕРИРУЕМЫЙ IPC-мост (не редактировать)
    │   │   │   ├── assets/         # Статические ресурсы фронтенда
    │   │   │   │   ├── locales/    # JSON-файлы локализации (12 языков)
    │   │   │   │   ├── images/     # Изображения и иконки
    │   │   │   │   └── backgrounds/ # Фоны панели управления
    │   │   │   ├── App.tsx         # Корневой компонент с маршрутизацией
    │   │   │   ├── main.tsx        # Точка входа React
    │   │   │   └── index.css       # Глобальные стили + Tailwind
    │   │   ├── index.html          # Входной HTML для Vite
    │   │   ├── vite.config.ts      # Конфигурация Vite (Tailwind, base: './')
    │   │   ├── tsconfig*.json      # Конфигурации TypeScript
    │   │   └── package.json        # Зависимости фронтенда
    │   │
    │   ├── Services/Core/          # Только Electron-специфичные адаптеры
    │   │   ├── Ipc/                # IPC-атрибуты, контракты и мост
    │   │   └── Platform/           # Electron-реализация буфера обмена
    │   │
    │   ├── Properties/             # Метаданные сборки/пакета и платформенные ресурсы
    │   │   ├── linux/              # Metainfo Linux + метаданные flatpak
    │   │   ├── macos/              # Info.plist macOS и ресурсы
    │   │   └── windows/            # Иконки и ресурсы Windows
    │   │
    │   └── wwwroot/                # Скомпилированный фронтенд (генерируется при сборке)
    │       ├── index.html          # Точка входа для продакшена
    │       └── assets/             # Скомпилированные JS/CSS бандлы
    │
    ├── HyPrism.Core/               # Core-логика лаунчера без зависимости от Electron
    │   ├── Bootstrapper.cs         # Общий DI-граф с точкой расширения для host
    │   ├── Models/                 # Модели конфигурации, профилей, инстансов, новостей и игры
    │   ├── Core/                   # App, infrastructure, integration и platform abstractions
    │   ├── Game/                   # Auth, assets, downloads, instances, launch, mods и versions
    │   ├── User/                   # Профили, identity, скины, токены и Hytale auth
    │   └── HyPrism.Core.csproj
    │
    ├── HyPrism.Desktop/            # Нативный desktop-хост на Avalonia 12
    │   ├── Views/                   # AXAML-представления и оболочка окна
    │   ├── ViewModels/              # MVVM-состояние, новости и оркестрация сервисов
    │   ├── Styles/                  # Общая визуальная система Avalonia
    │   │   ├── Tokens.axaml         # Цветовая палитра и цветовые токены контролов
    │   │   └── Styles.axaml         # Общие селекторы, шаблоны и переходы
    │   ├── Localization/            # Адаптер существующих JSON-локализаций
    │   ├── Assets/Fonts/            # Встроенные файлы Google Sans
    │   └── Assets/Icons/            # Зафиксированные AXAML-геометрии Material Symbols
    │
    └── HyPrism.IpcGen/             # Генератор IPC (на базе Roslyn)
    │   ├── Program.cs              # Точка входа генератора
    │   ├── HyPrism.IpcGen.csproj   # Файл проекта генератора
    │   └── ...                     # Логика анализа Roslyn
│
├── Tests/                        # Тестовые проекты вне production-исходников
│   ├── HyPrism.Core.Tests/       # Unit-тесты общей логики лаунчера
│   └── HyPrism.Desktop.Tests/    # Headless-тесты Avalonia layout/render
│
├── Scripts/                      # Скрипты сборки и утилиты
│   ├── publish.sh                # Скрипт публикации для платформ
│   └── update-flathub-manifest.sh  # Обновление метаданных Flatpak
│
├── .github/                      # Рабочие процессы GitHub
│   └── workflows/
│       ├── build.yml              # CI конвейер сборки
│       ├── release.yml           # Автоматизация релизов
│       └── flathub_push.yml      # Интеграция с Flathub
│
└── Docs/                         # Документация
    ├── English/                   # Документация на английском
    └── Russian/                   # Документация на русском
```

## Важные замечания

- **`HyPrism.Core`** владеет общими моделями, сервисами и DI-графом лаунчера и не зависит от Electron
- **`HyPrism.Core`** хранит домены сервисов прямо в корне проекта (`Core/`, `Game/` и `User/`), без промежуточной директории `Services/`
- **`HyPrism.Launcher`** теперь является тонким legacy-host с запуском Electron, IPC-транспортом/контрактами и Electron-адаптером буфера обмена
- **`Tests/`** содержит `HyPrism.Core.Tests` и `HyPrism.Desktop.Tests`; production-проекты остаются в `Sources/`
- **`HyPrism.Desktop`** содержит нативную оболочку, вертикальный срез запуска/установки с Dashboard, подключённый к сервису раздел «Новости» с адаптивным reader и адаптивный service-backed раздел Settings; Instances, Mods и Profiles пока остаются заглушками
- **`Sources/HyPrism.Launcher/Frontend/src/lib/ipc.ts`** автоматически генерируется проектом `HyPrism.IpcGen` — никогда не редактируйте вручную
- **`Sources/HyPrism.Launcher/Frontend/src/assets/`** содержит все статические ресурсы фронтенда (изображения, локализации, фоны)
- **`Sources/HyPrism.Launcher/wwwroot/`** генерируется во время сборки — не редактируйте вручную
- **Папки экземпляров** хранятся как `{branch}/{guid}` (например, `release/abc123-...`)
- **Папки-плейсхолдеры** (например, `latest/` без бинарников клиента) игнорируются при обнаружении экземпляров и не превращаются в реальные экземпляры автоматически
