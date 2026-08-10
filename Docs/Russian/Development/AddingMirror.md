<!--
Copyright (C) 2026 HyPrism Launcher
SPDX-License-Identifier: GPL-3.0-only
-->

# Добавление встроенного зеркала

Это руководство для контрибьюторов, которые хотят добавить новое зеркало в **стандартный набор** HyPrism — зеркала, автоматически генерируемые при первом запуске через `MirrorLoaderService.GetDefaultMirrors()`.

> **Справочник по схеме зеркал, плейсхолдерам URL, методам обнаружения версий и семантике `diffBasedBranches`** см. в [Руководстве по зеркалам для пользователей](../User/Mirrors.md). Здесь описан только рабочий процесс контрибьютора.

---

## Требования

Перед добавлением зеркала в стандартный набор убедитесь:

1. Зеркало **публично доступно** и надёжно (разумный аптайм).
2. Оператор зеркала **дал согласие** на включение в HyPrism.
3. Вы **проверили**, что зеркало отдаёт валидные `.pwr`-файлы, совпадающие с официальными патчами Hytale.
4. Вы определили, какой **тип источника** подходит: `pattern` или `json-index` (см. [Руководство по зеркалам — Типы источников](../User/Mirrors.md#тип-источника-pattern)).

## Обзор архитектуры

```
MirrorLoaderService.cs          ← Генерирует дефолты + загружает *.mirror.json
    ↓ создаёт
JsonMirrorSource.cs             ← Универсальный IVersionSource для всех зеркал
    ↑ реализует
IVersionSource.cs               ← Единый интерфейс (official + зеркала)
    ↑ используется
VersionService.cs               ← Оркестрирует все источники, кэширует результаты
```

**Ключевые файлы:**

| Файл | Назначение |
|------|-----------|
| `Sources/HyPrism.Core/Game/Sources/MirrorLoaderService.cs` | Определения дефолтных зеркал (`GetDefaultMirrors()`), логика загрузки |
| `Sources/HyPrism.Core/Game/Sources/JsonMirrorSource.cs` | Обрабатывает оба типа: `pattern` и `json-index` |
| `Sources/HyPrism.Core/Game/Sources/IVersionSource.cs` | Интерфейс + вспомогательные типы |
| `Models/MirrorMeta.cs` | Модель данных для схемы `.mirror.json` |

Создавать новый C#-класс для каждого зеркала **не нужно**. Все зеркала используют `JsonMirrorSource`.

## Пошаговое руководство

### 1. Соберите информацию о зеркале

Определите:

| Вопрос | Пример |
|--------|--------|
| ID зеркала (уникальный, lowercase, без пробелов) | `mymirror` |
| Отображаемое имя | `MyMirror` |
| Структура URL для полных сборок | `https://cdn.example.com/{os}/{arch}/{branch}/0/{version}.pwr` |
| Структура URL для diff-патчей (если есть) | `https://cdn.example.com/{os}/{arch}/{branch}/{from}/{to}.pwr` |
| Как обнаружить доступные версии | JSON API / HTML autoindex / статический список |
| Используются ли нестандартные имена веток/ОС в URL? | `prerelease` вместо `pre-release`, `mac` вместо `darwin` |
| Какие ветки имеют только diff'ы (нет полных сборок)? | `[]` или `["pre-release"]` |
| URL для проверки доступности (ping) | `https://cdn.example.com/health` |

### 2. Добавьте запись `MirrorMeta`

Откройте `Sources/HyPrism.Core/Game/Sources/MirrorLoaderService.cs` и добавьте новую запись в список, возвращаемый `GetDefaultMirrors()`.

**Для pattern-зеркала:**

```csharp
// MyMirror — json-api based
new() {
    SchemaVersion = 1,
    Id = "mymirror",
    Name = "MyMirror",
    Description = "Community mirror hosted by MyMirror (cdn.example.com)",
    Priority = 103,  // Следующий после существующих зеркал
    Enabled = true,
    SourceType = "pattern",
    Pattern = new MirrorPatternConfig
    {
        FullBuildUrl = "{base}/{os}/{arch}/{branch}/0/{version}.pwr",
        DiffPatchUrl = "{base}/{os}/{arch}/{branch}/{from}/{to}.pwr",
        BaseUrl = "https://cdn.example.com/hytale",
        VersionDiscovery = new VersionDiscoveryConfig
        {
            Method = "json-api",
            Url = "{base}/api/versions?branch={branch}&os={os}&arch={arch}",
            JsonPath = "versions"
        },
        DiffBasedBranches = new List<string>()
    },
    SpeedTest = new MirrorSpeedTestConfig
    {
        PingUrl = "https://cdn.example.com/health"
    },
    Cache = new MirrorCacheConfig
    {
        IndexTtlMinutes = 30,
        SpeedTestTtlMinutes = 60
    }
}
```

**Для json-index-зеркала:**

```csharp
// MyIndexMirror — single API index
new() {
    SchemaVersion = 1,
    Id = "myindexmirror",
    Name = "MyIndexMirror",
    Description = "Community mirror hosted by MyIndexMirror (api.example.com)",
    Priority = 104,
    Enabled = true,
    SourceType = "json-index",
    JsonIndex = new MirrorJsonIndexConfig
    {
        ApiUrl = "https://api.example.com/index",
        RootPath = "hytale",
        Structure = "grouped",  // или "flat"
        PlatformMapping = new Dictionary<string, string>
        {
            ["darwin"] = "mac"  // только если API использует нестандартные имена
        },
        FileNamePattern = new FileNamePatternConfig
        {
            Full = "v{version}-{os}-{arch}.pwr",
            Diff = "v{from}~{to}-{os}-{arch}.pwr"
        },
        DiffBasedBranches = new List<string>()
    },
    SpeedTest = new MirrorSpeedTestConfig
    {
        PingUrl = "https://api.example.com/index"
    },
    Cache = new MirrorCacheConfig
    {
        IndexTtlMinutes = 30,
        SpeedTestTtlMinutes = 60
    }
}
```

### 3. Выберите правильный приоритет

Существующие приоритеты дефолтных зеркал:

| Тип зеркала | Приоритет |
|-------------|-----------|
| HTML Autoindex-стиль | 100 |
| JSON API-стиль | 101 |
| JSON Index-стиль | 102 |

Используйте следующий доступный номер (например, `103`). Меньше = выше приоритет. Официальный Hytale всегда `0`.

### 4. Настройте маппинги (при необходимости)

Если зеркало использует нестандартные имена в URL или ключах API:

```csharp
// Имя ветки отличается от внутреннего "pre-release"
BranchMapping = new Dictionary<string, string>
{
    ["pre-release"] = "prerelease"
},

// Имя ОС отличается от внутреннего "darwin"
OsMapping = new Dictionary<string, string>
{
    ["darwin"] = "macos"
},

// Ключ платформы отличается в json-index API
PlatformMapping = new Dictionary<string, string>
{
    ["darwin"] = "mac"
}
```

### 5. Соберите и протестируйте

```bash
# Должно собраться без ошибок
dotnet build

# Запустите и убедитесь, что зеркало появилось в логах
dotnet run
# Ищите: "Loaded mirror: MyMirror (mymirror) [priority=103, type=pattern]"
```

**Чек-лист ручного тестирования:**

- [ ] Лаунчер запускается без ошибок
- [ ] Зеркало отображается в выводе `MirrorLoader` в логах
- [ ] `<каталог-данных>/Mirrors/mymirror.mirror.json` генерируется при чистом запуске (удалите папку `Mirrors/` для теста)
- [ ] Список версий загружается для ветки `release`
- [ ] Список версий загружается для ветки `pre-release`
- [ ] URL полной сборки корректно открывается (проверьте в браузере)
- [ ] URL diff-патча корректно открывается (если настроен `diffPatchUrl`)
- [ ] Сгенерированный JSON-файл валиден и использует UTF-8 (без `\uXXXX`-escape для читаемых символов)

### 6. Обновите документацию

Обновите следующие файлы:

| Файл | Что добавить |
|------|-------------|
| `Docs/English/User/Mirrors.md` | Аннотированный пример в разделе «Built-in mirror examples» |
| `Docs/Russian/User/Mirrors.md` | То же самое на русском |

Обновлять справочник по схеме не нужно — он универсален и покрывает все зеркала.

### 7. Отправьте Pull Request

Чек-лист PR:

- [ ] `MirrorLoaderService.cs` — новая запись в `GetDefaultMirrors()`
- [ ] `dotnet build` проходит без ошибок и предупреждений
- [ ] Протестировано с чистой папкой `Mirrors/` (удалена и перегенерирована)
- [ ] Зеркало проверено хотя бы на одной платформе (Linux, Windows или macOS)
- [ ] Английская пользовательская документация обновлена (`Docs/English/User/Mirrors.md`)
- [ ] Русская пользовательская документация обновлена (`Docs/Russian/User/Mirrors.md`)
- [ ] Согласие оператора зеркала указано в описании PR

## Частые ошибки

### Забыт `DiffBasedBranches`

Если оставить это поле неустановленным (`null`), оно по умолчанию создаст пустой список — обычно это правильно. Добавляйте ветки сюда, только если зеркало **действительно не имеет полных сборок** для данной ветки.

Подробнее в [Руководстве по зеркалам — diffBasedBranches](../User/Mirrors.md#что-такое-diffbasedbranches).

### Неправильный формат `jsonPath`

Поле `jsonPath` в `VersionDiscoveryConfig` поддерживает ровно три формата:

| jsonPath | Формат ответа API |
|----------|------------------|
| `"$root"` | `[1, 2, 3]` |
| `"versions"` | `{"versions": [1, 2, 3]}` |
| `"items[].version"` | `{"items": [{"version": 1}, ...]}` |

Другие JSON-path выражения **не поддерживаются**. Если API зеркала не соответствует ни одному из них, придётся расширять `JsonMirrorSource.ParseVersionsFromJson()`.

### Regex для HTML autoindex

Regex в `htmlPattern` должен быть валидным .NET-регулярным выражением. Протестируйте его на реальном HTML-ответе. Частые проблемы:
- Двойное экранирование кавычек в C#-строках (используйте `@""` verbatim-строки)
- Различная HTML-структура между nginx, Apache и другими веб-серверами

### Юникод в генерируемом JSON

`MirrorLoaderService` использует `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` чтобы избежать `\uXXXX`-escape в генерируемых файлах. Не меняйте это — это делает `.mirror.json` файлы читаемыми для человека.

## Расширение системы зеркал

Если новое зеркало требует возможностей, не покрываемых текущей схемой (например, аутентификация, кастомный протокол), может потребоваться:

1. Добавить новые поля в `MirrorMeta` / `MirrorPatternConfig` / `MirrorJsonIndexConfig` в `Models/MirrorMeta.cs`
2. Обработать их в `JsonMirrorSource.cs`
3. Увеличить `SchemaVersion`, если изменение ломает совместимость
4. Обновить справочник по схеме в пользовательской документации
5. Обеспечить обратную совместимость — старые `.mirror.json` без новых полей должны продолжать работать (используйте nullable-типы со значениями по умолчанию)
