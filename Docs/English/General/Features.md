<!--
Copyright (C) 2026 HyPrism Launcher
SPDX-License-Identifier: GPL-3.0-only
-->

# Features

## Instance Management

- **GUID-based instance folders** — Installations are stored under branch + instance ID (for example `Instances/release/<instance-guid>/`)
- **Multi-instance support** — Run different game versions, mod configurations, or test setups simultaneously
- **Instance metadata** — Track installation state, version, patch status, and mod list per instance
- **Instance deletion** — Clean removal of game files with confirmation
- **Instance import/export** — Import instances from ZIP archives or PWR patch files, export instances for backup/sharing
- **PWR file support** — Import full game builds directly from `.pwr` files (official Hytale patch format)
- **Dashboard quick switcher** — Icon-only current instance selector (left of Play) opens a dropdown with instance icons + names

## Authentication

- **Hytale account login** — Official authentication through Hytale's OAuth flow
- **Profile system** — Multiple player profiles with nickname and UUID
- **Skin backup/restore** — Per-profile skin backups stored in `Profiles/` folder
- **Session management** — Secure token storage and automatic refresh

## Onboarding

- **First-launch wizard** — Guided setup experience on initial run:
  1. **Splash** — Welcome screen with branding
  2. **Language selection** — Choose from 12 supported languages
  3. **Authentication** — Hytale account login
  4. **Profile setup** — Create your first player profile
  5. **Settings** — Configure GPU preference and other options

## Game Management

- **Download & install** — Fetch game files from official CDN
- **Patching** — Apply official and community patches
- **Launch** — Start the game with configured settings
- **GPU preference** — Select graphics adapter (auto/dedicated/integrated)
- **Resolution & RAM** — Customize game window size and memory allocation
- **Auto-close** — Option to close launcher when game starts

## Modding

- **CurseForge integration** — Browse, search, and download mods from CurseForge
- **Installed mods view** — Manage installed modifications per instance
- **Mod metadata** — Version, author, description, download count, dependencies
- **Default mods location** — Uses `UserData/Mods` inside each instance (`Instances/<branch>/v<version>/UserData/Mods`)

## Social & News

- **Hytale news feed** — Latest official news and announcements
- **Discord Rich Presence** — Show game status and playtime in Discord

## User Interface

- **Modern dark UI** — Custom frameless window with glass-morphism design
- **Framer Motion animations** — Smooth page transitions and micro-interactions
- **Responsive layout** — Sidebar navigation, dashboard, news feed, settings, mod manager
- **Accent color customization** — Personalize the theme color
- **Settings-integrated logs** — Read launcher logs directly from the Settings sidebar Logs tab
- **Dashboard action clarity** — Educational badge and instance switcher controls use solid styling for better readability

## Platform Integration

- **macOS app menu actions** — Native menu bar entries for launcher navigation (Settings, Instances, Quit)

## Internationalization

- **12 languages supported:**
  - English (en-US)
  - Russian (ru-RU)
  - German (de-DE)
  - Spanish (es-ES)
  - French (fr-FR)
  - Japanese (ja-JP)
  - Korean (ko-KR)
  - Portuguese (pt-BR)
  - Turkish (tr-TR)
  - Ukrainian (uk-UA)
  - Chinese Simplified (zh-CN)
  - Belarusian (be-BY)
- **Runtime switching** — Change language without restart
- **Nested keys** — Structured localization with placeholder support

## Updates

- **Auto-updates** — Launcher self-update via GitHub releases
- **Pre-release channel** — Opt-in to receive pre-release builds
- **Release notes** —before updating

## Developer Features

- **IPC code generation** — C# annotations generate typed TypeScript IPC client
- **MSBuild pipeline** — Automated build: `npm install → IPC codegen → Vite build → copy dist`
- **Serilog logging** — Structured file logging with console output
- **Flatpak packaging** — Linux packaging with manifest
