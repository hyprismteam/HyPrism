<!--
Copyright (C) 2026 HyPrism Launcher
SPDX-License-Identifier: GPL-3.0-only
-->

# Локализация

HyPrism поддерживает 12 языков с переключением в реальном времени.

## Файлы локалей

**Расположение:** `Assets/Locales/{code}.json`

**Поддерживаемые языки:**

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

## Формат файла

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

**Правила:**
- Вложенные ключи: `dashboard.welcome`
- Подстановки: `{0}`, `{1}` и т.д.
- Метаданные с префиксом `_` (например, `_langName`, `_langCode`)

## IPC-каналы

| Канал | Тип | Описание |
|-------|-----|----------|
| `hyprism:i18n:get` | invoke | Получить все переводы для текущего языка |
| `hyprism:i18n:current` | invoke | Получить код текущего языка |
| `hyprism:i18n:set` | invoke | Сменить язык (принимает `{ language: "code" }`) |
| `hyprism:i18n:languages` | invoke | Получить список доступных языков |

## Использование на бэкенде

```csharp
// Получить перевод
var text = LocalizationService.Instance.Translate("button.play");

// Сменить язык
_configService.Configuration.Language = "ru-RU";
LocalizationService.Instance.LoadLanguage("ru-RU");
```

## Использование на фронтенде

```typescript
import { ipc } from '../lib/ipc';

// Получить все переводы
const translations = await ipc.i18n.get();

// Сменить язык
await ipc.i18n.set({ language: 'ru-RU' });

// Получить доступные языки
const langs = await ipc.i18n.languages();
// → [{ code: 'en-US', name: 'English' }, ...]
```
