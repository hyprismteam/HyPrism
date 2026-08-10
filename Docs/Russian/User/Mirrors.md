<!--
Copyright (C) 2026 HyPrism Launcher
SPDX-License-Identifier: GPL-3.0-only
-->

# Руководство по зеркалам

HyPrism использует **data-driven систему зеркал** для загрузки игровых файлов. Вместо жёстко прописанных URL лаунчер читает определения зеркал из файлов `.mirror.json` в директории `Mirrors/`. Это руководство объясняет, как работает система, как добавлять зеркала и как их настраивать.

---

## Содержание

- [Обзор](#обзор)
- [Как работают зеркала](#как-работают-зеркала)
- [Добавление зеркал](#добавление-зеркал)
  - [Способ 1: Через интерфейс настроек (рекомендуется)](#способ-1-через-интерфейс-настроек-рекомендуется)
  - [Способ 2: Вручную через JSON-файл](#способ-2-вручную-через-json-файл)
- [Расположение каталога данных](#расположение-каталога-данных)
- [Структура папки Mirrors](#структура-папки-mirrors)
- [Справочник по схеме зеркала](#справочник-по-схеме-зеркала)
  - [Общие поля](#общие-поля)
  - [Тип источника: pattern](#тип-источника-pattern)
  - [Тип источника: json-index](#тип-источника-json-index)
  - [Конфигурация speed test](#конфигурация-speed-test)
  - [Конфигурация кэша](#конфигурация-кэша)
- [Справочник плейсхолдеров URL](#справочник-плейсхолдеров-url)
- [Методы обнаружения версий](#методы-обнаружения-версий)
  - [json-api](#метод-json-api)
  - [html-autoindex](#метод-html-autoindex)
  - [static-list](#метод-static-list)
- [Что такое diffBasedBranches](#что-такое-diffbasedbranches)
- [Примеры конфигураций зеркал (с пояснениями)](#примеры-конфигураций-зеркал-с-пояснениями)
  - [HTML Autoindex-стиль (pattern + html-autoindex)](#html-autoindex-стиль-pattern--html-autoindex)
  - [JSON API-стиль (pattern + json-api)](#json-api-стиль-pattern--json-api)
  - [JSON Index-стиль (json-index)](#json-index-стиль-json-index)
- [Практические примеры](#практические-примеры)
  - [Пример 1: pattern-зеркало с JSON API](#пример-1-pattern-зеркало-с-json-api)
  - [Пример 2: pattern-зеркало с HTML-листингом каталога](#пример-2-pattern-зеркало-с-html-листингом-каталога)
  - [Пример 3: json-index зеркало](#пример-3-json-index-зеркало)
  - [Пример 4: простое статическое зеркало](#пример-4-простое-статическое-зеркало)
- [Отключение, включение и удаление зеркал](#отключение-включение-и-удаление-зеркал)
- [Решение проблем](#решение-проблем)
- [Часто задаваемые вопросы](#часто-задаваемые-вопросы)

---

## Обзор

HyPrism всегда сначала пытается скачать файлы с **официальных серверов Hytale** (если у вас есть официальный аккаунт). Если официальная загрузка недоступна или медленная, лаунчер автоматически тестирует доступные зеркала и использует лучшее на основе приоритета и скорости.

Зеркала — это серверы сообщества, которые хранят копии игровых файлов Hytale. Они полезны, когда:

- У вас нет официального аккаунта Hytale
- Официальные серверы недоступны или плохо работают в вашем регионе
- Вы хотите более быструю загрузку с географически более близкого сервера
- Вы хотите хостить файлы самостоятельно для своего сообщества

> **Примечание:** По умолчанию зеркала не настроены. Вы должны добавить зеркала вручную через Настройки или поместив `.mirror.json` файлы в папку Mirrors.

## Как работают зеркала

1. При запуске лаунчер читает все файлы `*.mirror.json` из директории `Mirrors/` и создаёт из них источники загрузки.
2. Зеркала с `"enabled": false` пропускаются.
3. Источники **сортируются по приоритету** (меньшее число = выше приоритет). Официальный источник Hytale имеет приоритет `0`, зеркала обычно начинаются с `100`.
4. Когда нужна загрузка, лаунчер автоматически выбирает лучший доступный источник.

## Добавление зеркал

### Способ 1: Через интерфейс настроек (рекомендуется)

1. Откройте **Настройки → Загрузки**.
2. Нажмите кнопку **«Добавить зеркало»**.
3. Введите базовый URL зеркала (например, `https://mirror.example.com/hytale/patches`).
4. Лаунчер **автоматически определит** конфигурацию зеркала (JSON API, HTML autoindex или структуру директорий).
5. Если определение успешно, зеркало сразу добавляется и готово к использованию.

Система автоопределения пробует несколько стратегий:
- **JSON Index API** — единый эндпоинт с полным индексом файлов
- **JSON API** — эндпоинт списка версий
- **HTML Autoindex** — листинг каталога Apache/Nginx
- **Структура директорий** — URL на основе паттерна с поддиректориями OS/arch/branch

### Способ 2: Вручную через JSON-файл

1. Перейдите в каталог данных → папку `Mirrors/`.
2. Создайте новый файл, например `my-mirror.mirror.json`.
3. Вставьте шаблон зеркала (см. ниже) и заполните данные вашего сервера.
4. Перезапустите лаунчер.

## Расположение каталога данных

| Платформа | Путь |
|-----------|------|
| **Windows** | `%APPDATA%\HyPrism\` |
| **Linux** | `~/.local/share/HyPrism/` |
| **macOS** | `~/Library/Application Support/HyPrism/` |

Вы также можете открыть эту директорию из лаунчера: **Настройки → Данные → Открыть папку лаунчера**.

## Структура папки Mirrors

```
<каталог-данных>/
└── Mirrors/
    └── my-custom.mirror.json      ← ваше пользовательское зеркало
```

Соглашение об именовании файлов: `<id-зеркала>.mirror.json`. Имя файла не влияет на функциональность — лаунчер использует поле `id` внутри JSON — но совпадение имён облегчает управление.

> **Совет:** Когда вы добавляете зеркало через интерфейс настроек, лаунчер автоматически создаёт файл `.mirror.json` за вас.

---

## Справочник по схеме зеркала
{
  "schemaVersion": 1,
  "id": "my-mirror",
  "name": "Моё зеркало",
  "priority": 110,
  "enabled": true,
  "sourceType": "pattern",
  "pattern": {
    "fullBuildUrl": "{base}/{os}/{arch}/{branch}/0/{version}.pwr",
    "baseUrl": "https://my-server.example.com/hytale",
    "versionDiscovery": {
      "method": "static-list",
      "staticVersions": [1, 2, 3, 4, 5, 6, 7, 8]
    }
  }
}
```

Это самое простое определение зеркала. Оно говорит лаунчеру:
- Скачивать полные сборки с `https://my-server.example.com/hytale/{os}/{arch}/{branch}/0/{version}.pwr`
- Доступны версии с 1 по 8
- Нет diff-патчей, нет speed test, TTL кэша по умолчанию

---

## Справочник по схеме зеркала

### Общие поля

Эти поля применяются к каждому зеркалу вне зависимости от типа источника.

| Поле | Тип | Обязательно | По умолчанию | Описание |
|------|-----|-------------|--------------|----------|
| `schemaVersion` | int | Да | `1` | Версия схемы. Всегда ставьте `1`. |
| `id` | string | **Да** | — | Уникальный идентификатор. Используется внутренне для кэширования и логирования. Должен быть уникальным среди всех зеркал. |
| `name` | string | Нет | `""` | Отображаемое имя. |
| `description` | string | Нет | `null` | Необязательное описание. |
| `priority` | int | Нет | `100` | Приоритет. Меньшее число = выше приоритет. Официальный источник — `0`. Зеркала рекомендуется ставить `≥ 100`. |
| `enabled` | bool | Нет | `true` | `false` — отключить зеркало без удаления файла. |
| `sourceType` | string | **Да** | `"pattern"` | Должен быть `"pattern"` или `"json-index"`. Определяет, какой блок конфигурации используется. |
| `pattern` | object | Условно | `null` | Обязателен при `sourceType: "pattern"`. См. [Конфигурация pattern](#тип-источника-pattern). |
| `jsonIndex` | object | Условно | `null` | Обязателен при `sourceType: "json-index"`. См. [Конфигурация json-index](#тип-источника-json-index). |
| `speedTest` | object | Нет | `{}` | Конфигурация тестирования скорости. См. [Speed test](#конфигурация-speed-test). |
| `cache` | object | Нет | `{}` | Конфигурация TTL кэша. См. [Кэш](#конфигурация-кэша). |

### Тип источника: pattern

**Тип `"pattern"`** предназначен для зеркал, где известна структура URL. Лаунчер строит URL-адреса загрузки из шаблонов, подставляя плейсхолдеры, и обнаруживает доступные версии через отдельный endpoint.

#### Поля pattern

| Поле | Тип | Обязательно | По умолчанию | Описание |
|------|-----|-------------|--------------|----------|
| `fullBuildUrl` | string | Да | `"{base}/{os}/{arch}/{branch}/0/{version}.pwr"` | Шаблон URL для полных сборок. См. [Плейсхолдеры URL](#справочник-плейсхолдеров-url). |
| `diffPatchUrl` | string | Нет | `null` | Шаблон URL для дифференциальных патчей (обновление между версиями). Если не указан, доступны только полные сборки. |
| `signatureUrl` | string | Нет | `null` | Шаблон URL для файлов подписей `.sig`. Используется для верификации. |
| `baseUrl` | string | Да | `""` | Базовый URL, подставляемый в плейсхолдер `{base}`. |
| `versionDiscovery` | object | Да | — | Настройка обнаружения доступных версий. См. [Методы обнаружения версий](#методы-обнаружения-версий). |
| `osMapping` | object | Нет | `null` | Переопределяет внутренние имена ОС в URL. Пример: `{"darwin": "macos"}`. |
| `branchMapping` | object | Нет | `null` | Переопределяет внутренние имена веток в URL. Пример: `{"pre-release": "prerelease"}`. |
| `diffBasedBranches` | string[] | Нет | `[]` | Ветки, в которых доступны **только** diff-патчи (нет полных сборок). См. [Что такое diffBasedBranches](#что-такое-diffbasedbranches). |

#### Как разрешаются шаблоны URL

Для такой конфигурации:
```json
{
  "fullBuildUrl": "{base}/{os}/{arch}/{branch}/0/{version}.pwr",
  "diffPatchUrl": "{base}/{os}/{arch}/{branch}/{from}/{to}.pwr",
  "baseUrl": "https://my-server.example.com/hytale/patches"
}
```

Полная сборка версии 8 для Linux x64 release:
```
https://my-server.example.com/hytale/patches/linux/x64/release/0/8.pwr
```

Diff-патч с версии 7 на 8:
```
https://my-server.example.com/hytale/patches/linux/x64/release/7/8.pwr
```

#### Пример branchMapping

Если ваш сервер использует `prerelease` вместо `pre-release` в URL:
```json
{
  "branchMapping": {
    "pre-release": "prerelease"
  }
}
```

URL `{base}/{os}/{arch}/{branch}/0/{version}.pwr` для ветки `pre-release` разрешится в:
```
https://example.com/linux/x64/prerelease/0/8.pwr
```
вместо:
```
https://example.com/linux/x64/pre-release/0/8.pwr
```

#### Пример osMapping

Если ваш сервер использует `macos` вместо `darwin`:
```json
{
  "osMapping": {
    "darwin": "macos"
  }
}
```

### Тип источника: json-index

**Тип `"json-index"`** предназначен для зеркал, которые предоставляют единый API-endpoint, возвращающий полный индекс файлов с URL для скачивания. Лаунчер загружает индекс один раз и извлекает из него всю информацию о версиях и URL для загрузки.

#### Поля json-index

| Поле | Тип | Обязательно | По умолчанию | Описание |
|------|-----|-------------|--------------|----------|
| `apiUrl` | string | Да | `""` | URL API-endpoint'а, возвращающего полный индекс файлов. |
| `rootPath` | string | Нет | `"hytale"` | Имя корневого свойства в JSON-ответе. |
| `structure` | string | Нет | `"flat"` | Как организованы файлы: `"flat"` или `"grouped"`. См. ниже. |
| `platformMapping` | object | Нет | `null` | Маппинг внутренних имён ОС в ключи платформ JSON. Пример: `{"darwin": "mac"}`. |
| `fileNamePattern` | object | Нет | — | Паттерны для парсинга имён файлов. См. ниже. |
| `diffBasedBranches` | string[] | Нет | `[]` | Ветки, в которых доступны **только** diff-патчи (нет полных сборок). См. [Что такое diffBasedBranches](#что-такое-diffbasedbranches). |

#### Паттерны имён файлов

| Поле | Тип | По умолчанию | Описание |
|------|-----|--------------|----------|
| `fileNamePattern.full` | string | `"v{version}-{os}-{arch}.pwr"` | Паттерн для имён файлов полных сборок. |
| `fileNamePattern.diff` | string | `"v{from}~{to}-{os}-{arch}.pwr"` | Паттерн для имён файлов diff-патчей. |

#### Структура: flat vs. grouped

**`"flat"`** — файлы организованы как `ветка.платформа → { имя_файла: url }`:
```json
{
  "hytale": {
    "release.linux.x64": {
      "v8-linux-x64.pwr": "https://example.com/v8-linux-x64.pwr"
    }
  }
}
```

**`"grouped"`** — файлы организованы как `ветка.платформа → группа → { имя_файла: url }`, где группы — обычно `"base"` (полные сборки) и `"patch"` (diff-патчи):
```json
{
  "hytale": {
    "release.linux.x64": {
      "base": {
        "v8-linux-x64.pwr": "https://example.com/base/v8-linux-x64.pwr"
      },
      "patch": {
        "v7~8-linux-x64.pwr": "https://example.com/patch/v7~8-linux-x64.pwr"
      }
    }
  }
}
```

### Конфигурация speed test

| Поле | Тип | По умолчанию | Описание |
|------|-----|--------------|----------|
| `speedTest.pingUrl` | string | `null` | URL для быстрой проверки доступности (HTTP HEAD запрос). Укажите любой URL на вашем сервере. |
| `speedTest.pingTimeoutSeconds` | int | `5` | Таймаут пинга в секундах. |
| `speedTest.speedTestSizeBytes` | int | `10485760` (10 МБ) | Объём данных для скачивания при тесте скорости. |

Если `pingUrl` не указан, лаунчер пропустит тестирование скорости для этого зеркала.

### Конфигурация кэша

| Поле | Тип | По умолчанию | Описание |
|------|-----|--------------|----------|
| `cache.indexTtlMinutes` | int | `30` | Время жизни кэша индекса версий (минуты). |
| `cache.speedTestTtlMinutes` | int | `60` | Время жизни кэша результата теста скорости (минуты). |

---

## Справочник плейсхолдеров URL

Эти плейсхолдеры доступны в шаблонах `fullBuildUrl`, `diffPatchUrl`, `signatureUrl` и `versionDiscovery.url`:

| Плейсхолдер | Описание | Примеры значений |
|-------------|----------|------------------|
| `{base}` | Заменяется на `baseUrl` | `https://my-server.example.com/hytale` |
| `{os}` | Операционная система | `linux`, `windows`, `darwin` |
| `{arch}` | Архитектура процессора | `x64`, `arm64` |
| `{branch}` | Ветка игры (после маппинга) | `release`, `pre-release` |
| `{version}` | Номер целевой версии | `1`, `2`, ..., `8` |
| `{from}` | Исходная версия для diff-патча | `7` |
| `{to}` | Целевая версия для diff-патча | `8` |

> **Примечание:** Если определены `osMapping` или `branchMapping`, вместо внутренних имён используются mapped-значения. Например, при `{"darwin": "mac"}` в `osMapping`, `{os}` на macOS будет `mac`, а не `darwin`.

---

## Методы обнаружения версий

Для `sourceType: "pattern"` лаунчеру нужно знать, какие версии доступны. Это настраивается через блок `versionDiscovery`.

### Метод: json-api

Загружает JSON-endpoint и извлекает номера версий.

```json
{
  "versionDiscovery": {
    "method": "json-api",
    "url": "{base}/versions?branch={branch}&os={os}&arch={arch}",
    "jsonPath": "items[].version"
  }
}
```

**`jsonPath`** поддерживает три формата:

| Формат | Ожидаемый JSON | Пример |
|--------|----------------|--------|
| `"$root"` | Корень — массив чисел | `[1, 2, 3, 4, 5]` |
| `"versions"` | Свойство содержит массив чисел | `{"versions": [1, 2, 3]}` |
| `"items[].version"` | Массив объектов с полем version | `{"items": [{"version": 1}, {"version": 2}]}` |

**Пример** — метод JSON API:
```json
{
  "method": "json-api",
  "url": "{base}/launcher/patches/{branch}/versions?os_name={os}&arch={arch}",
  "jsonPath": "items[].version"
}
```

Типичный API по адресу `https://example.com/launcher/patches/release/versions?os_name=linux&arch=x64` возвращает:
```json
{
  "items": [
    { "version": 1, "size": 1234567 },
    { "version": 2, "size": 2345678 },
    ...
  ]
}
```

### Метод: html-autoindex

Парсит HTML-листинг каталога (nginx autoindex или Apache directory listing) для извлечения имён файлов и номеров версий.

```json
{
  "versionDiscovery": {
    "method": "html-autoindex",
    "url": "{base}/{os}/{arch}/{branch}/0/",
    "htmlPattern": "<a\\s+href=\"(\\d+)\\.pwr\">\\d+\\.pwr</a>\\s+\\S+\\s+\\S+\\s+(\\d+)",
    "minFileSizeBytes": 1048576
  }
}
```

| Поле | Описание |
|------|----------|
| `htmlPattern` | Регулярное выражение, применяемое к HTML. Группа захвата 1 = номер версии. Группа захвата 2 (опционально) = размер файла в байтах. |
| `minFileSizeBytes` | Если regex захватывает размер файла (группа 2), файлы меньше этого размера игнорируются. Полезно для фильтрации неполных загрузок. По умолчанию: `0` (без фильтрации). |

**Пример** — nginx autoindex. Типичный HTML-листинг каталога по адресу `https://example.com/hytale/patches/linux/x64/release/0/` выглядит так:

```html
<pre>
<a href="1.pwr">1.pwr</a>       2025-01-15 12:34    123456789
<a href="2.pwr">2.pwr</a>       2025-01-20 15:00    234567890
...
</pre>
```

Regex `<a\s+href="(\d+)\.pwr">\d+\.pwr</a>\s+\S+\s+\S+\s+(\d+)` захватывает:
- Группа 1: `1`, `2` и т.д. (номер версии)
- Группа 2: `123456789` и т.д. (размер файла)

Файлы меньше 1 МБ (`1048576` байт) игнорируются.

### Метод: static-list

Жёстко заданный список номеров версий. Сетевой запрос для обнаружения версий не нужен.

```json
{
  "versionDiscovery": {
    "method": "static-list",
    "staticVersions": [1, 2, 3, 4, 5, 6, 7, 8]
  }
}
```

Используйте этот метод, когда:
- Ваше зеркало содержит фиксированный набор версий
- Ваш сервер не предоставляет API для списка версий
- Вы хотите максимально простую конфигурацию

> **Недостаток:** Необходимо вручную обновлять список при появлении новых версий.

---

## Что такое diffBasedBranches

Поле `diffBasedBranches` сообщает лаунчеру, в каких ветках доступны **только** дифференциальные патчи и **нет** полных сборок.

### Что такое полные сборки и diff-патчи?

- **Полная сборка** — Полный пакет игры для определённой версии (например, версия 8). Скачиваете один раз для установки.
- **Diff-патч** — Инкрементальное обновление от одной версии к другой (например, версия 7 → 8). Гораздо меньше полной сборки, но требует уже установленной предыдущей версии.

### Как `diffBasedBranches` влияет на поведение

| `diffBasedBranches` | Полные сборки доступны? | Diff-патчи доступны? | Первая установка | Обновления |
|---------------------|------------------------|---------------------|------------------|------------|
| Ветка **не** в списке | ✅ Да | ✅ Да (если задан `diffPatchUrl`) | Скачивает полную сборку | Использует diff, если доступен, иначе полную сборку |
| Ветка **в** списке | ❌ Нет | ✅ Да | Применяет цепочку diff'ов от версии 0 | Использует diff-патчи |

### Пример

```json
{
  "diffBasedBranches": ["pre-release"]
}
```

Это означает:
- **release** ветка: Есть полные сборки. Пользователи могут скачать любую версию напрямую. Diff-патчи тоже доступны для обновлений.
- **pre-release** ветка: Только diff-патчи. Чтобы установить версию 22, лаунчер скачивает патчи `0→1`, `1→2`, ..., `21→22` и применяет их последовательно.

### Когда использовать

- Ставьте `[]` (пустой массив), если ваше зеркало хранит полные сборки для всех веток. Большинство зеркал должны использовать это.
- Добавьте имя ветки, если ваше зеркало **действительно** не имеет полных сборок для этой ветки — только diff'ы между версиями.
- Даже если `diffBasedBranches` пуст, лаунчер **всё равно будет использовать** diff-патчи для обновлений, когда настроен `diffPatchUrl`. Поле влияет только на доступность **полных сборок**.

---

## Примеры конфигураций зеркал (с пояснениями)

Ниже приведены примеры конфигураций зеркал, демонстрирующие различные подходы. Это иллюстративные шаблоны — вы можете использовать их как отправную точку при настройке собственных зеркал.

### HTML Autoindex-стиль (pattern + html-autoindex)

Этот пример показывает зеркало, которое хостит файлы на веб-сервере с включённым nginx/Apache autoindex. Лаунчер парсит HTML-листинг каталога для обнаружения версий.

```json
{
  "schemaVersion": 1,
  "id": "my-html-mirror",
  "name": "Моё HTML-зеркало",
  "description": "Зеркало с HTML-листингом каталога для обнаружения версий",
  "priority": 100,
  "enabled": true,
  "sourceType": "pattern",
  "pattern": {
    "fullBuildUrl": "{base}/{os}/{arch}/{branch}/0/{version}.pwr",
    "diffPatchUrl": "{base}/{os}/{arch}/{branch}/{from}/{to}.pwr",
    "signatureUrl": "{base}/{os}/{arch}/{branch}/0/{version}.pwr.sig",
    "baseUrl": "https://my-server.example.com/hytale/patches",
    "versionDiscovery": {
      "method": "html-autoindex",
      "url": "{base}/{os}/{arch}/{branch}/0/",
      "htmlPattern": "<a\\s+href=\"(\\d+)\\.pwr\">\\d+\\.pwr</a>\\s+\\S+\\s+\\S+\\s+(\\d+)",
      "minFileSizeBytes": 1048576
    },
    "diffBasedBranches": []
  },
  "speedTest": {
    "pingUrl": "https://my-server.example.com/hytale/patches"
  },
  "cache": {
    "indexTtlMinutes": 30,
    "speedTestTtlMinutes": 60
  }
}
```

**Ключевые моменты:**
- **Структура файлов на сервере:** `patches/{os}/{arch}/{branch}/0/{version}.pwr` для полных сборок, `patches/{os}/{arch}/{branch}/{from}/{to}.pwr` для diff'ов.
- **Обнаружение версий:** Парсит HTML по адресу `.../release/0/`, чтобы найти файлы `*.pwr` и извлечь номера версий.
- **`diffBasedBranches: []`** — И release, и pre-release имеют полные сборки.
- **`signatureUrl`** — Предоставляет файлы `.pwr.sig` для верификации загрузок.
- **`diffPatchUrl`** задан — diff-патчи доступны для всех веток (более быстрые обновления).

**Итоговые URL (Linux x64, release, версия 8):**
| Тип | URL |
|-----|-----|
| Полная сборка | `https://my-server.example.com/hytale/patches/linux/x64/release/0/8.pwr` |
| Diff 7→8 | `https://my-server.example.com/hytale/patches/linux/x64/release/7/8.pwr` |
| Подпись | `https://my-server.example.com/hytale/patches/linux/x64/release/0/8.pwr.sig` |
| Список версий | `https://my-server.example.com/hytale/patches/linux/x64/release/0/` |

### JSON API-стиль (pattern + json-api)

Этот пример показывает зеркало, которое предоставляет JSON API для обнаружения версий. Обратите внимание на использование `branchMapping` для настройки URL.

```json
{
  "schemaVersion": 1,
  "id": "my-json-api-mirror",
  "name": "Моё JSON API зеркало",
  "description": "Зеркало с JSON API для обнаружения версий",
  "priority": 101,
  "enabled": true,
  "sourceType": "pattern",
  "pattern": {
    "fullBuildUrl": "{base}/launcher/patches/{os}/{arch}/{branch}/0/{version}.pwr",
    "diffPatchUrl": "{base}/launcher/patches/{os}/{arch}/{branch}/{from}/{to}.pwr",
    "baseUrl": "https://my-server.example.com",
    "versionDiscovery": {
      "method": "json-api",
      "url": "{base}/launcher/patches/{branch}/versions?os_name={os}&arch={arch}",
      "jsonPath": "items[].version"
    },
    "branchMapping": {
      "pre-release": "prerelease"
    },
    "diffBasedBranches": []
  },
  "speedTest": {
    "pingUrl": "https://my-server.example.com/health"
  },
  "cache": {
    "indexTtlMinutes": 30,
    "speedTestTtlMinutes": 60
  }
}
```

**Ключевые моменты:**
- **`branchMapping`** — Преобразует `pre-release` в `prerelease` в URL.
- **Обнаружение версий:** Метод `json-api`. API возвращает `{ "items": [{ "version": 1 }, ...] }`, а `jsonPath: "items[].version"` извлекает номера.
- **Нет `signatureUrl`** — Это зеркало не предоставляет файлы подписей.
- **`diffPatchUrl`** задан — diff-патчи доступны.

**Итоговые URL (Linux x64, ветка pre-release, версия 22):**
| Тип | URL |
|-----|-----|
| Полная сборка | `https://my-server.example.com/launcher/patches/linux/x64/prerelease/0/22.pwr` |
| Diff 21→22 | `https://my-server.example.com/launcher/patches/linux/x64/prerelease/21/22.pwr` |
| Список версий | `https://my-server.example.com/launcher/patches/prerelease/versions?os_name=linux&arch=x64` |

> Обратите внимание, как `{branch}` разрешился в `prerelease` (а не `pre-release`) благодаря `branchMapping`.

### JSON Index-стиль (json-index)

Этот пример показывает зеркало с единым API, возвращающим полный индекс файлов с прямыми URL для скачивания. Это самый простой вариант для операторов зеркал, так как лаунчеру нужен только один эндпоинт.

```json
{
  "schemaVersion": 1,
  "id": "my-json-index-mirror",
  "name": "Моё JSON Index зеркало",
  "description": "Зеркало с JSON index API для обнаружения файлов",
  "priority": 102,
  "enabled": true,
  "sourceType": "json-index",
  "jsonIndex": {
    "apiUrl": "https://my-server.example.com/api/files",
    "rootPath": "hytale",
    "structure": "grouped",
    "platformMapping": {
      "darwin": "mac"
    },
    "fileNamePattern": {
      "full": "v{version}-{os}-{arch}.pwr",
      "diff": "v{from}~{to}-{os}-{arch}.pwr"
    },
    "diffBasedBranches": ["pre-release"]
  },
  "speedTest": {
    "pingUrl": "https://my-server.example.com/api/files"
  },
  "cache": {
    "indexTtlMinutes": 30,
    "speedTestTtlMinutes": 60
  }
}
```

**Ключевые моменты:**
- **`sourceType: "json-index"`** — Шаблоны URL не нужны. API возвращает полные URL для скачивания.
- **`structure: "grouped"`** — Файлы разделены на группы `"base"` (полные сборки) и `"patch"` (diff'ы).
- **`platformMapping: {"darwin": "mac"}`** — macOS хранится как `mac` в API.
- **`fileNamePattern`** — Указывает лаунчеру, как парсить имена файлов: `v8-linux-x64.pwr` (полная сборка) и `v7~8-linux-x64.pwr` (diff).
- **`diffBasedBranches: ["pre-release"]`** — Ветка pre-release имеет только diff-патчи (нет полных сборок). Ветка release имеет и полные сборки, и diff'ы.

**Пример ответа API (упрощённый):**
```json
{
  "hytale": {
    "release.linux.x64": {
      "base": {
        "v1-linux-x64.pwr": "https://my-server.example.com/.../v1-linux-x64.pwr",
        "v2-linux-x64.pwr": "https://my-server.example.com/.../v2-linux-x64.pwr",
        ...
        "v8-linux-x64.pwr": "https://my-server.example.com/.../v8-linux-x64.pwr"
      },
      "patch": {
        "v1~2-linux-x64.pwr": "https://my-server.example.com/.../v1~2-linux-x64.pwr",
        "v2~3-linux-x64.pwr": "https://my-server.example.com/.../v2~3-linux-x64.pwr",
        ...
        "v7~8-linux-x64.pwr": "https://my-server.example.com/.../v7~8-linux-x64.pwr"
      }
    },
    "pre-release.linux.x64": {
      "patch": {
        "v0~1-linux-x64.pwr": "https://my-server.example.com/.../v0~1-linux-x64.pwr",
        "v1~2-linux-x64.pwr": "https://my-server.example.com/.../v1~2-linux-x64.pwr",
        ...
        "v21~22-linux-x64.pwr": "https://my-server.example.com/.../v21~22-linux-x64.pwr"
      }
    }
  }
}
```

---

## Практические примеры

### Пример 1: pattern-зеркало с JSON API

**Сценарий:** Вы хостите игровые файлы на `https://files.myguild.com/hytale/` и имеете простое API на `/api/versions`, возвращающее номера версий.

**Шаг 1:** Определите структуру URL.
- Полные сборки: `https://files.myguild.com/hytale/{os}/{arch}/{branch}/full/{version}.pwr`
- Diff-патчей нет.

**Шаг 2:** Определите формат ответа API.
- `GET https://files.myguild.com/hytale/api/versions?branch=release` возвращает:
  ```json
  {"versions": [1, 2, 3, 4, 5, 6, 7, 8]}
  ```

**Шаг 3:** Создайте `myguild.mirror.json`:

```json
{
  "schemaVersion": 1,
  "id": "myguild",
  "name": "Зеркало моей гильдии",
  "description": "Приватное зеркало для нашей гильдии",
  "priority": 105,
  "enabled": true,
  "sourceType": "pattern",
  "pattern": {
    "fullBuildUrl": "{base}/{os}/{arch}/{branch}/full/{version}.pwr",
    "baseUrl": "https://files.myguild.com/hytale",
    "versionDiscovery": {
      "method": "json-api",
      "url": "{base}/api/versions?branch={branch}",
      "jsonPath": "versions"
    }
  },
  "speedTest": {
    "pingUrl": "https://files.myguild.com/hytale/api/versions"
  }
}
```

**Шаг 4:** Поместите файл в `<каталог-данных>/Mirrors/` и перезапустите лаунчер.

### Пример 2: pattern-зеркало с HTML-листингом каталога

**Сценарий:** Вы хостите файлы на веб-сервере с nginx autoindex или Apache directory listing.

**Шаг 1:** Структура директорий на сервере:
```
/var/www/hytale/
├── linux/x64/release/
│   ├── 1.pwr
│   ├── 2.pwr
│   └── ...
└── windows/x64/release/
    ├── 1.pwr
    └── ...
```

**Шаг 2:** Обращение к `https://mirror.example.com/hytale/linux/x64/release/` показывает HTML-листинг каталога с тегами `<a>` для каждого файла.

**Шаг 3:** Создайте `my-nginx.mirror.json`:

```json
{
  "schemaVersion": 1,
  "id": "my-nginx",
  "name": "Моё Nginx-зеркало",
  "priority": 120,
  "enabled": true,
  "sourceType": "pattern",
  "pattern": {
    "fullBuildUrl": "{base}/{os}/{arch}/{branch}/{version}.pwr",
    "baseUrl": "https://mirror.example.com/hytale",
    "versionDiscovery": {
      "method": "html-autoindex",
      "url": "{base}/{os}/{arch}/{branch}/",
      "htmlPattern": "<a\\s+href=\"(\\d+)\\.pwr\">"
    }
  }
}
```

> **Совет:** В regex `htmlPattern` нужна только одна группа захвата (номер версии). Группа с размером файла необязательна и используется для фильтрации маленьких/неполных файлов.

### Пример 3: json-index зеркало

**Сценарий:** Ваш сервер предоставляет единый API-endpoint, возвращающий плоский JSON-индекс всех файлов.

**Шаг 1:** Ваше API по адресу `https://api.mymirror.com/index` возвращает:
```json
{
  "game": {
    "release.linux.x64": {
      "v1-linux-x64.pwr": "https://cdn.mymirror.com/release/v1-linux-x64.pwr",
      "v2-linux-x64.pwr": "https://cdn.mymirror.com/release/v2-linux-x64.pwr"
    },
    "release.windows.x64": {
      "v1-windows-x64.pwr": "https://cdn.mymirror.com/release/v1-windows-x64.pwr"
    }
  }
}
```

**Шаг 2:** Создайте `mymirror.mirror.json`:

```json
{
  "schemaVersion": 1,
  "id": "mymirror",
  "name": "Моё CDN-зеркало",
  "priority": 108,
  "enabled": true,
  "sourceType": "json-index",
  "jsonIndex": {
    "apiUrl": "https://api.mymirror.com/index",
    "rootPath": "game",
    "structure": "flat",
    "fileNamePattern": {
      "full": "v{version}-{os}-{arch}.pwr"
    }
  },
  "speedTest": {
    "pingUrl": "https://cdn.mymirror.com"
  }
}
```

**Ключевые детали:**
- `rootPath` — `"game"`, потому что файлы вложены в свойство `"game"` в ответе.
- `structure` — `"flat"`, потому что нет групп `"base"`/`"patch"`.
- Паттерн `diff` не задан в `fileNamePattern`, так как на этом зеркале только полные сборки.

### Пример 4: простое статическое зеркало

**Сценарий:** У вас простой файловый сервер с известными версиями. Нет API, нет листинга каталога.

Создайте `static.mirror.json`:

```json
{
  "schemaVersion": 1,
  "id": "my-static",
  "name": "Статический файловый сервер",
  "priority": 150,
  "enabled": true,
  "sourceType": "pattern",
  "pattern": {
    "fullBuildUrl": "{base}/game-v{version}-{os}-{arch}.pwr",
    "baseUrl": "https://files.example.com",
    "versionDiscovery": {
      "method": "static-list",
      "staticVersions": [5, 6, 7, 8]
    }
  }
}
```

Это разрешится в:
```
https://files.example.com/game-v8-linux-x64.pwr
```

> **Помните:** С `static-list` вам нужно вручную обновлять массив `staticVersions` при выходе новых версий.

---

## Отключение, включение и удаление зеркал

| Действие | Как сделать |
|----------|------------|
| **Временно отключить** | Откройте файл `.mirror.json` и установите `"enabled": false`. Перезапустите лаунчер. |
| **Включить обратно** | Установите `"enabled": true`. Перезапустите лаунчер. |
| **Удалить навсегда** | Удалите файл `.mirror.json` из папки `Mirrors/`. Перезапустите лаунчер. |
| **Сбросить всё к умолчаниям** | Удалите всю папку `Mirrors/`. Лаунчер пересоздаст дефолтные при следующем запуске. |

---

## Решение проблем

### Зеркало не появляется в лаунчере

1. Проверьте, что файл находится в правильной директории `Mirrors/`.
2. Проверьте, что расширение файла — именно `.mirror.json` (не просто `.json`).
3. Проверьте валидность JSON (используйте JSON-валидатор).
4. Посмотрите логи лаунчера (**Настройки → Логи**) на наличие ошибок от `MirrorLoader`.
5. Убедитесь, что `"enabled"` установлен в `true`.
6. Убедитесь, что `"id"` не пустой.

### Зеркало загружается, но скачивание не работает

1. Проверьте, что `baseUrl` или `apiUrl` доступен из вашей сети.
2. Для pattern-зеркал вручную соберите URL по шаблону и откройте его в браузере для проверки.
3. Проверьте, что `osMapping` и `branchMapping` соответствуют структуре URL вашего сервера.
4. Проверьте логи лаунчера на наличие HTTP-кодов ошибок.

### Список версий пуст

1. Для `json-api`: проверьте, что `url` возвращает валидный JSON и что `jsonPath` правильно указывает на массив версий.
2. Для `html-autoindex`: проверьте, что `htmlPattern` совпадает с HTML-структурой листинга каталога вашего сервера.
3. Для `static-list`: проверьте, что `staticVersions` — непустой массив.
4. Проверьте `minFileSizeBytes` — если значение слишком большое, могут отфильтроваться все файлы.

### Diff-патчи не работают

1. Убедитесь, что `diffPatchUrl` задан (для pattern-зеркал) или `fileNamePattern.diff` задан (для json-index зеркал).
2. Проверьте, что diff-файлы реально существуют на сервере.
3. Проверьте, что плейсхолдеры `{from}` и `{to}` в шаблоне URL совпадают с тем, как файлы называются на сервере.

---

## Часто задаваемые вопросы

**В: Могу ли я использовать зеркала без официального источника Hytale?**
О: Лаунчер всегда сначала пробует официальный источник (приоритет 0). Зеркала используются как fallback. Отключить официальный источник нельзя.

**В: Нужно ли перезапускать лаунчер после редактирования файла зеркала?**
О: Да. Файлы зеркал читаются один раз при запуске.

**В: Что будет, если два зеркала имеют одинаковый `id`?**
О: Оба будут загружены, что может привести к непредсказуемому поведению. Всегда используйте уникальные ID.

**В: Что будет, если два зеркала имеют одинаковый `priority`?**
О: Оба будут загружены. Порядок между ними определяется порядком перечисления файлов в файловой системе, который не гарантирован. Используйте различные приоритеты для предсказуемого поведения.

**В: Могу ли я иметь зеркало только для одной ветки?**
О: Да. Если ваше зеркало хостит только файлы release, лаунчер просто не найдёт версии pre-release на вашем зеркале и переключится на другие источники.

**В: Какие имена ОС использует лаунчер внутренне?**
О: `linux`, `windows`, `darwin` (для macOS). Используйте `osMapping`, если ваш сервер использует другие имена.

**В: Какие имена архитектур использует лаунчер?**
О: `x64`, `arm64`. Используются как есть, если не переопределены.

**В: Какие имена веток использует лаунчер?**
О: `release`, `pre-release`. Используйте `branchMapping`, если ваш сервер использует другие имена (например, `prerelease`).

**В: Могу ли я изменить дефолтные зеркала?**
О: Да. После первого запуска вы можете свободно редактировать сгенерированные файлы `.mirror.json`. Лаунчер не перезаписывает их.
