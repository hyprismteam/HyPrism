<!--
Copyright (C) 2026 HyPrism Launcher
SPDX-License-Identifier: GPL-3.0-only
-->

# Coding Standards

## C# Conventions

### Naming

| Type | Convention | Example |
|------|------------|---------|
| Classes, Methods, Properties | PascalCase | `GameSessionService`, `LoadAsync()` |
| Local variables | camelCase | `userName`, `isValid` |
| Private fields | _camelCase | `_configService`, `_isLoading` |
| Interfaces | IPrefix | `IConfigService` |
| Constants | PascalCase | `MaxRetryCount` |

### Code Style

- **Indentation:** 4 spaces (no tabs)
- **Braces:** Allman style (opening brace on new line)
- **Async methods:** Always suffix with `Async`
- **Nullability:** `<Nullable>enable</Nullable>` — avoid `!` operator
- **File-scoped namespaces:** `namespace HyPrism.Services.Core;`

```csharp
// ✅ Correct
public async Task<Config> LoadAsync()
{
    if (condition)
    {
        await DoWorkAsync();
    }
}

// ❌ Wrong — K&R braces, missing Async suffix
public async Task<Config> Load() {
    if (condition) {
        await DoWork();
    }
}
```

## TypeScript / React Conventions

### Naming

| Type | Convention | Example |
|------|------------|---------|
| Components | PascalCase function | `export function TitleBar()` |
| Hooks | camelCase, use-prefix | `useGame()`, `useState()` |
| Files | PascalCase for components | `TitleBar.tsx`, `Dashboard.tsx` |
| Utility files | camelCase | `ipc.ts`, `helpers.ts` |
| CSS variables | kebab-case | `--bg-darkest`, `--accent` |
| IPC channels | kebab-colon | `hyprism:game:launch` |

### Code Style

- **Components:** Named function exports (never default export for components)
- **State:** React hooks only (`useState`, `useContext`, `useEffect`)
- **Styling:** Tailwind utility classes + CSS custom properties
- **No class components** — functional components only
- **No separate CSS modules** — use Tailwind + `index.css` custom properties

```tsx
// ✅ Correct
export function MyComponent({ title }: { title: string }) {
  const [isOpen, setIsOpen] = useState(false);
  return <div className="flex items-center">{title}</div>;
}

// ❌ Wrong — default export, class component
export default class MyComponent extends React.Component { }
```

## Anti-Patterns

```csharp
// ❌ Referencing old Avalonia UI from services
using Avalonia.Controls;

// ❌ Using legacy localization files
var text = File.ReadAllText("assets/game-lang/en.lang");
```

```tsx
// ❌ Direct Node.js / Electron API in renderer
const fs = require('fs');

// ❌ Hardcoded colors instead of theme tokens
<div style={{ color: '#7C5CFC' }}>  // Use var(--accent)

// ❌ Editing auto-generated ipc.ts manually
```
