<!--
Copyright (C) 2026 HyPrism Launcher
SPDX-License-Identifier: GPL-3.0-only
-->

# Руководство по фронтенду

## Обзор

Фронтенд — это React 18 SPA, собранный с помощью Vite 5 и TypeScript 5.3. Он запускается внутри Electron BrowserWindow, загружаемого из `file://wwwroot/index.html`.

## Стек

| Библиотека | Назначение |
|------------|-----------|
| React 18.2.0 | UI-фреймворк |
| TypeScript 5.3.0 | Типобезопасность |
| Vite 5.0.0 | Инструмент сборки |
| TailwindCSS 3.4.0 | Утилитарный CSS |
| Framer Motion 11.0.0 | Анимации |
| Lucide React 0.300.0 | Иконки |
| React Router DOM | Клиентская маршрутизация |
| i18next / react-i18next | Интернационализация |

## Страницы

| Страница | Файл | Маршрут | Описание |
|----------|------|---------|----------|
| Панель управления | `pages/DashboardPage.tsx` | `/` | Запуск игры, прогресс, статус |
| Новости | `pages/NewsPage.tsx` | `/news` | Лента новостей Hytale |
| Экземпляры | `pages/InstancesPage.tsx` | `/instances` | Менеджер экземпляров с вкладками (Контент, Миры) |
| Профили | `pages/ProfilesPage.tsx` | `/profiles` | Управление профилями |
| Настройки | `pages/settings/SettingsPage.tsx` | `/settings` | Настройки с вкладками |
| Логи | `pages/LogsPage.tsx` | `/logs` | Просмотр логов |
| Онбординг | `pages/onboarding/OnboardingPage.tsx` | Мастер первого запуска |

## Компоненты

| Компонент | Файл | Описание |
|-----------|------|----------|
| DockMenu | `components/layout/DockMenu.tsx` | Панель навигации/dock меню |
| BackgroundImage | `components/layout/BackgroundImage.tsx` | Обработчик динамического фона |
| MusicPlayer | `components/layout/MusicPlayer.tsx` | Плеер фоновой музыки |
| UpdateOverlay | `components/layout/UpdateOverlay.tsx` | Оверлей доступного обновления |

## UI-примитивы

Чтобы интерфейс оставался единообразным и поддерживаемым, используйте общие примитивы из `Sources/HyPrism.Launcher/Frontend/src/components/ui/`:

- **PageContainer** (`components/ui/PageContainer.tsx`) — единая максимальная ширина, центрирование и адаптивные отступы для основных страниц
- **SettingsHeader** (`components/ui/SettingsHeader.tsx`) — унифицированный заголовок секции/страницы (title + опциональное описание, опциональный слот для действий)
- **SelectionCard** (`components/ui/SelectionCard.tsx`) — переиспользуемая карточка-выбор с вариантами оформления, слотом иконки, состоянием selected и обработчиком клика

### Общие Controls (единый источник правды)

Для большинства интерактивных элементов (кнопки, иконки-кнопки, таб-переключатели, скролл-области и lightbox) используйте:

- **Controls** (`components/ui/Controls.tsx`) — стабильный barrel-export (реализации лежат в `components/ui/controls/`)

Этот файл специально централизует внешний вид/поведение UI, чтобы не плодить множество одноразовых стилей кнопок.

**Что использовать**
- `Button` — базовая кнопка для большинства действий
- `IconButton` — квадратные действия только с иконкой (refresh/copy/export и т.д.)
- `LinkButton` — кнопка в стиле ссылки для текстовых действий (без одноразовых `className` кнопок)
- `LauncherActionButton` — градиентные основные действия (Play/Stop/Download/Update/Select) с "лаунчерным" шрифтом/весом
- `SegmentedControl` — переключатели в стиле табов с бегунком (как в Instances)
- `AccentSegmentedControl` — обёртка над `SegmentedControl`, автоматически применяющая текущий accent-стиль (используется для фильтров в Logs и табов Instances)
- `Switch` — accent-reactive переключатель
- `ScrollArea` — единообразный overflow + опциональный `thin-scrollbar`
- `ImageLightbox` — просмотр скриншотов по центру с навигацией `1/3 < >`
- `DropdownTriggerButton` — стандартный триггер dropdown (label + chevron + состояние open)
- `MenuActionButton` — полноширинные пункты меню для hover-меню (например, Worlds overlay)
- `MenuItemButton` — полноширинные пункты для context/popover меню (заменяет одноразовые `button className="..."` в меню)
- `ModalFooterActions` — стандартная строка действий в футере модалки (отступы + граница + фон)

**Размеры IconButton**
- Используйте `IconButton size="sm" | "md" | "lg"` вместо ручных `h-/w-` классов.

**Варианты IconButton**
- Используйте `variant="overlay"` для навигации в скриншотах/lightbox (без glass hover).

**Правило**
- Если хочется написать новую кнопку с кастомным `className="...rounded...hover..."` — лучше использовать `Button`/`IconButton` из `@/components/ui/Controls`.

**Правки controls**
- Если нужно поменять конкретный элемент, правьте роль-модуль в `components/ui/controls/`, а `Controls.tsx` держите как тонкий re-export.

## Разделение больших страниц

Если файл страницы становится слишком большим, выносите связные блоки UI в отдельную папку рядом со страницей и переносите общие типы туда же.

- Пример: `InstancesPage` использует `Frontend/src/pages/instances/` для вынесенных компонентов и общих типов.

## Создание компонента

```tsx
import { ipc } from '../lib/ipc';
import type { Profile } from '../lib/ipc';

interface Props {
  profileId: string;
}

export function ProfileCard({ profileId }: Props) {
  const [profile, setProfile] = useState<Profile | null>(null);

  useEffect(() => {
    ipc.profile.get().then(setProfile);
  }, [profileId]);

  if (!profile) return <div>Loading...</div>;
  return (
    <div className="p-4 rounded-xl" style={{ backgroundColor: 'var(--bg-light)' }}>
      {profile.name}
    </div>
  );
}
```

## Темизация

Все цвета темы определены как CSS custom properties в `Frontend/src/index.css`:

```css
:root {
  --bg-darkest: #0D0D10;    /* Фон приложения */
  --bg-dark: #14141A;        /* Боковая панель, заголовок */
  --bg-medium: #1C1C26;      /* Карточки */
  --bg-light: #252533;       /* Приподнятые поверхности */
  --bg-lighter: #2E2E40;     /* Границы, полоса прокрутки */
  --text-primary: #F0F0F5;   /* Основной текст */
  --text-secondary: #A0A0B8; /* Второстепенный текст */
  --text-muted: #6B6B80;     /* Приглушённый / неактивный */
  --accent: #7C5CFC;         /* Основной акцент (фиолетовый) */
  --accent-hover: #6A4AE8;   /* Акцент при наведении */
  --success: #4ADE80;
  --warning: #FBBF24;
  --error: #F87171;
}
```

**Использование:**
- Классы Tailwind для компоновки/отступов: `className="flex items-center gap-2 p-4"`
- CSS-переменные для цветов темы: `style={{ color: 'var(--accent)' }}`
- Никогда не используйте захардкоженные hex-цвета — всегда CSS-переменные

## Анимации (Framer Motion)

Переходы между страницами и микровзаимодействия используют Framer Motion:

```tsx
import { motion } from 'framer-motion';

export function MyPage() {
  return (
    <motion.div
      initial={{ opacity: 0, y: 20 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.5, ease: 'easeOut' }}
    >
      ...
    </motion.div>
  );
}
```

## Использование IPC

Весь IPC доступен через автогенерируемый объект `ipc`:

```typescript
import { ipc } from '../lib/ipc';

// Invoke (запрос/ответ)
const settings = await ipc.settings.get();

// Send (без ожидания ответа)
ipc.windowCtl.minimize();

// Подписка на события
ipc.game.onProgress((data) => setProgress(data.progress));

// Открыть URL
ipc.browser.open('https://example.com');
```

## Провайдеры контекста

Состояние игры управляется через `GameContext`:

```tsx
const { isPlaying, launch, cancel } = useGame();
```

Добавляйте новые контексты в `Sources/HyPrism.Launcher/Frontend/src/contexts/` для состояния других доменов.

## Иконки

Все иконки берутся из **Lucide React** — без пользовательских SVG-файлов:

```tsx
import { Settings, Download, Play } from 'lucide-react';

<Settings size={18} style={{ color: 'var(--text-secondary)' }} />
```
