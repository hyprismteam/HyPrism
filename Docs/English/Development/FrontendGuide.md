# Frontend Guide

## Overview

The frontend is a React 18 SPA built with Vite 5 and TypeScript 5.3. It runs inside an Electron BrowserWindow loaded from `file://wwwroot/index.html`.

## Stack

| Library | Purpose |
|---------|---------|
| React 18.2.0 | UI framework |
| TypeScript 5.3.0 | Type safety |
| Vite 5.0.0 | Build tool |
| TailwindCSS 3.4.0 | Utility-first CSS |
| Framer Motion 11.0.0 | Animations |
| Lucide React 0.300.0 | Icons |
| React Router DOM | Client-side routing |
| i18next / react-i18next | Internationalization |

## Pages

| Page | File | Route | Description |
|------|------|-------|-------------|
| Dashboard | `pages/DashboardPage.tsx` | `/` | Game launch, progress, status |
| News | `pages/NewsPage.tsx` | `/news` | Hytale news feed |
| Instances | `pages/InstancesPage.tsx` | `/instances` | Game instance manager with tabs (Content, Worlds) |
| Profiles | `pages/ProfilesPage.tsx` | `/profiles` | Profile management |
| Settings | `pages/settings/SettingsPage.tsx` | `/settings` | App settings with tabs |
| Logs | `pages/LogsPage.tsx` | `/logs` | Log viewer |
| Onboarding | `pages/onboarding/OnboardingPage.tsx` | First-launch wizard |

## Components

| Component | File | Description |
|-----------|------|-------------|
| DockMenu | `components/layout/DockMenu.tsx` | Navigation dock/menu |
| BackgroundImage | `components/layout/BackgroundImage.tsx` | Dynamic background handler |
| MusicPlayer | `components/layout/MusicPlayer.tsx` | Background music player |
| UpdateOverlay | `components/layout/UpdateOverlay.tsx` | Update available overlay |

## UI Primitives

To keep pages consistent and maintainable, prefer the shared primitives in `Sources/HyPrism.Launcher/Frontend/src/components/ui/`:

- **PageContainer** (`components/ui/PageContainer.tsx`) — consistent max-width, centered layout, and responsive padding for all main pages
- **SettingsHeader** (`components/ui/SettingsHeader.tsx`) — unified section/page header (title + optional description, optional actions slot)
- **SelectionCard** (`components/ui/SelectionCard.tsx`) — reusable selectable “choice” card with variants, icon slot, selected state, and click handler

### Shared Controls (single source of truth)

For most interactive UI (buttons, icon buttons, tab-like segmented pills, scroll areas, and image lightbox), use:

- **Controls** (`components/ui/Controls.tsx`) — stable barrel export (implementations live in `components/ui/controls/`)

This file intentionally centralizes the “feel” of the app so we don’t end up with many one-off button styles.

**What to use**
- `Button` — default button for most actions
- `IconButton` — square icon-only actions (refresh/copy/export/etc.)
- `LinkButton` — link-style inline button for text actions (no custom `className` buttons)
- `LauncherActionButton` — gradient primary actions (Play/Stop/Download/Update/Select) with the launcher font/weight
- `SegmentedControl` — tab-like pill switchers with sliding indicator (same behavior as Instances tabs)
- `AccentSegmentedControl` — `SegmentedControl` wrapper that auto-applies the current accent styling (use for Logs filters and Instances tabs)
- `Switch` — accent-reactive toggle primitive
- `ScrollArea` — consistent overflow + optional `thin-scrollbar` styling
- `ImageLightbox` — centered screenshot viewer with `1/3 < >` navigation
- `DropdownTriggerButton` — standard dropdown trigger button (label + chevron + open state)
- `MenuActionButton` — full-width menu-row actions for hover menus (e.g., Worlds overlay)
- `MenuItemButton` — full-width menu-row actions for context menus / popover menus (replaces ad-hoc `button className="..."` in menus)
- `ModalFooterActions` — standard modal footer action row (spacing + border + background)

**IconButton sizing**
- Use `IconButton size="sm" | "md" | "lg"` instead of hardcoding `h-/w-` classes.

**IconButton variants**
- Use `variant="overlay"` for screenshot/lightbox navigation buttons (no glass hover).

**Rule of thumb**
- If you are about to write a new `className="...rounded...hover..."` button: stop and use `Button`/`IconButton` from `@/components/ui/Controls` instead.

**Editing controls**
- If you need to tweak a specific control, edit the role-based module in `components/ui/controls/` and keep `Controls.tsx` as a thin re-export.

## Creating a Component

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

## Theming

All theme colors are CSS custom properties defined in `Frontend/src/index.css`:

```css
:root {
  --bg-darkest: #0D0D10;    /* App background */
  --bg-dark: #14141A;        /* Sidebar, title bar */
  --bg-medium: #1C1C26;      /* Cards */
  --bg-light: #252533;       /* Elevated surfaces */
  --bg-lighter: #2E2E40;     /* Borders, scrollbar */
  --text-primary: #F0F0F5;   /* Main text */
  --text-secondary: #A0A0B8; /* Secondary text */
  --text-muted: #6B6B80;     /* Muted / disabled */
  --accent: #7C5CFC;         /* Primary accent (purple) */
  --accent-hover: #6A4AE8;   /* Accent hover */
  --success: #4ADE80;
  --warning: #FBBF24;
  --error: #F87171;
}
```

**Usage:**
- Tailwind classes for layout/spacing: `className="flex items-center gap-2 p-4"`
- CSS vars for theme colors: `style={{ color: 'var(--accent)' }}`
- Never hardcode hex colors — always use CSS variables

## Animations (Framer Motion)

Page transitions and micro-interactions use Framer Motion:

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

## IPC Usage

All IPC is accessed through the auto-generated `ipc` object:

```typescript
import { ipc } from '../lib/ipc';

// Invoke (request/reply)
const settings = await ipc.settings.get();

// Send (fire-and-forget)
ipc.windowCtl.minimize();

// Event subscription
ipc.game.onProgress((data) => setProgress(data.progress));

// Open URL
ipc.browser.open('https://example.com');
```

## Instance State Resilience

- The app auto-selects a fallback instance when the backend returns a list but no explicit selection.
- The fallback prefers an installed instance, then first available instance.
- When game-running state is true but running branch/version is temporarily unknown, instance controls treat the currently selected instance as the active one to avoid a permanently disabled Play button.

## Context Providers

Game state is managed via `GameContext`:

```tsx
const { isPlaying, launch, cancel } = useGame();
```

Add new contexts in `Sources/HyPrism.Launcher/Frontend/src/contexts/` for other domain state.

## Icons

All icons come from **Lucide React**

```tsx
import { Settings, Download, Play } from 'lucide-react';

<Settings size={18} style={{ color: 'var(--text-secondary)' }} />
```
