# Структура проекта

```
HyPrism/
├── HyPrism.sln                 # Файл решения (содержит папку Sources/)
│
└── Sources/                    # Основная директория с исходным кодом
    ├── HyPrism.Launcher/       # Главное приложение лаунчера (.NET 10 + Electron.NET)
    │   ├── Program.cs          # Точка входа: Console → Electron bootstrap
    │   ├── Bootstrapper.cs     # Настройка DI-контейнера
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
    │   ├── Services/               # Слой сервисов .NET
    │   │   ├── Core/               # Инфраструктурные сервисы
    │   │   │   ├── App/            # Сервисы приложения (Config, Settings, Update)
    │   │   │   ├── Infrastructure/ # Logger, ConfigService, LocalizationService
    │   │   │   ├── Integration/    # Внешние интеграции (Discord RPC, News, GitHub)
    │   │   │   ├── Ipc/            # IpcService - Центральный реестр IPC-каналов
    │   │   │   └── Platform/       # Платформозависимые утилиты
    │   │   ├── Game/               # Сервисы игровой логики
    │   │   │   ├── Instance/       # Управление экземплярами (InstanceService)
    │   │   │   ├── Launch/         # Запуск игры (GameLauncher, LaunchService)
    │   │   │   ├── Download/       # Управление загрузками
    │   │   │   ├── Mod/            # Управление модами
    │   │   │   ├── Auth/           # Аутентификация Hytale
    │   │   │   ├── Butler/         # Инструмент патчинга Butler
    │   │   │   ├── Asset/          # Ассеты игры и аватары
    │   │   │   └── Version/        # Управление версиями
    │   │   └── User/               # Сервисы, связанные с пользователем
    │   │       ├── ProfileService.cs   # Профили игроков (ник, UUID)
    │   │       ├── ProfileManagementService.cs  # Операции с профилями
    │   │       ├── SkinService.cs      # Управление скинами и резервное копирование
    │   │       └── HytaleAuthService.cs # Аутентификация аккаунта Hytale
    │   │
    │   ├── Models/                 # Модели данных (POCO)
    │   │   ├── Config.cs           # Модель конфигурации
    │   │   ├── Profile.cs          # Модель профиля игрока
    │   │   ├── InstanceMeta.cs     # Метаданные экземпляра
    │   │   └── ...
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
    ├── HyPrism.IpcGen/             # Генератор IPC (на базе Roslyn)
    │   ├── Program.cs              # Точка входа генератора
    │   ├── HyPrism.IpcGen.csproj   # Файл проекта генератора
    │   └── ...                     # Логика анализа Roslyn
    │
    └── HyPrism.Tests/              # Тестовый проект
        └── HyPrism.Tests.csproj
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

- **`Sources/HyPrism.Launcher/Frontend/src/lib/ipc.ts`** автоматически генерируется проектом `HyPrism.IpcGen` — никогда не редактируйте вручную
- **`Sources/HyPrism.Launcher/Frontend/src/assets/`** содержит все статические ресурсы фронтенда (изображения, локализации, фоны)
- **`Sources/HyPrism.Launcher/wwwroot/`** генерируется во время сборки — не редактируйте вручную
- **Папки экземпляров** хранятся как `{branch}/{guid}` (например, `release/abc123-...`)
- **Папки-плейсхолдеры** (например, `latest/` без бинарников клиента) игнорируются при обнаружении экземпляров и не превращаются в реальные экземпляры автоматически
