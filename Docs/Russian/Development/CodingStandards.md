<!--
Copyright (C) 2026 HyPrism Launcher
SPDX-License-Identifier: GPL-3.0-only
-->

# Стандарты кода

## Соглашения C#

### Именование

| Тип | Соглашение | Пример |
|-----|-----------|--------|
| Классы, методы, свойства | PascalCase | `GameSessionService`, `LoadAsync()` |
| Локальные переменные | camelCase | `userName`, `isValid` |
| Приватные поля | _camelCase | `_configService`, `_isLoading` |
| Интерфейсы | Префикс I | `IConfigService` |
| Константы | PascalCase | `MaxRetryCount` |

### Стиль кода

- **Отступы:** 4 пробела (без табуляции)
- **Фигурные скобки:** Стиль Allman (открывающая скобка на новой строке)
- **Асинхронные методы:** Всегда суффикс `Async`
- **Nullable:** `<Nullable>enable</Nullable>` — избегайте оператора `!`
- **Пространства имён уровня файла:** `namespace HyPrism.Services.Core;`

```csharp
// ✅ Правильно
public async Task<Config> LoadAsync()
{
    if (condition)
    {
        await DoWorkAsync();
    }
}

// ❌ Неправильно — K&R-скобки, отсутствует суффикс Async
public async Task<Config> Load() {
    if (condition) {
        await DoWork();
    }
}
```

## Соглашения TypeScript / React

### Именование

| Тип | Соглашение | Пример |
|-----|-----------|--------|
| Компоненты | Функции PascalCase | `export function TitleBar()` |
| Хуки | camelCase, префикс use | `useGame()`, `useState()` |
| Файлы | PascalCase для компонентов | `TitleBar.tsx`, `Dashboard.tsx` |
| Утилитарные файлы | camelCase | `ipc.ts`, `helpers.ts` |
| CSS-переменные | kebab-case | `--bg-darkest`, `--accent` |
| IPC-каналы | kebab-colon | `hyprism:game:launch` |

### Стиль кода

- **Компоненты:** Именованные функциональные экспорты (никогда default export для компонентов)
- **Состояние:** Только React-хуки (`useState`, `useContext`, `useEffect`)
- **Стилизация:** Утилитарные классы Tailwind + CSS custom properties
- **Никаких классовых компонентов** — только функциональные компоненты
- **Никаких отдельных CSS-модулей** — используйте Tailwind + custom properties из `index.css`

```tsx
// ✅ Правильно
export function MyComponent({ title }: { title: string }) {
  const [isOpen, setIsOpen] = useState(false);
  return <div className="flex items-center">{title}</div>;
}

// ❌ Неправильно — default export, классовый компонент
export default class MyComponent extends React.Component { }
```

## Антипаттерны

```csharp
// ❌ Обращение к старому Avalonia UI из сервисов
using Avalonia.Controls;

// ❌ Использование устаревших файлов локализации
var text = File.ReadAllText("assets/game-lang/en.lang");
```

```tsx
// ❌ Прямой доступ к Node.js / Electron API в рендерере
const fs = require('fs');

// ❌ Захардкоженные цвета вместо токенов темы
<div style={{ color: '#7C5CFC' }}>  // Используйте var(--accent)

// ❌ Ручное редактирование автогенерируемого ipc.ts
```
