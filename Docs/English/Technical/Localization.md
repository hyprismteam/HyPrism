<!--
Copyright (C) 2026 HyPrism Launcher
SPDX-License-Identifier: GPL-3.0-only
-->

# Localization

HyPrism supports 12 languages with runtime switching.

## Locale Files

**Location:** `Assets/Locales/{code}.json`

**Supported languages:**

| Code | Language |
|------|----------|
| en-US | English |
| ru-RU | Russian |
| de-DE | German |
| es-ES | Spanish |
| fr-FR | French |
| ja-JP | Japanese |
| ko-KR | Korean |
| pt-BR | Portuguese (Brazil) |
| tr-TR | Turkish |
| uk-UA | Ukrainian |
| zh-CN | Chinese (Simplified) |
| be-BY | Belarusian |

## File Format

```json
{
  "_langName": "English",
  "_langCode": "en-US",
  "button": {
    "play": "Play",
    "settings": "Settings"
  },
  "dashboard": {
    "welcome": "Welcome, {0}!"
  }
}
```

**Rules:**
- Nested keys: `dashboard.welcome`
- Placeholders: `{0}`, `{1}`, etc.
- Metadata fields prefixed with `_` (e.g. `_langName`, `_langCode`)

## IPC Channels

| Channel | Type | Description |
|---------|------|-------------|
| `hyprism:i18n:get` | invoke | Get all translations for current language |
| `hyprism:i18n:current` | invoke | Get current language code |
| `hyprism:i18n:set` | invoke | Change language (accepts `{ language: "code" }`) |
| `hyprism:i18n:languages` | invoke | Get list of available languages |

## Backend Usage

```csharp
// Get translation
var text = LocalizationService.Instance.Translate("button.play");

// Change language
_configService.Configuration.Language = "ru-RU";
LocalizationService.Instance.LoadLanguage("ru-RU");
```

## Frontend Usage

```typescript
import { ipc } from '../lib/ipc';

// Get all translations
const translations = await ipc.i18n.get();

// Change language
await ipc.i18n.set({ language: 'ru-RU' });

// Get available languages
const langs = await ipc.i18n.languages();
// → [{ code: 'en-US', name: 'English' }, ...]
```
