<!--
Copyright (C) 2026 HyPrism Launcher
SPDX-License-Identifier: GPL-3.0-only
-->

# Локализация

Локализацией интерфейса владеет desktop-приложение Avalonia. `HyPrism.Core` только сохраняет выбранный языковой тег в `Config.Language`: Core не загружает переводы, не содержит каталог локалей и не меняет культуру процесса.

## Файлы локалей

Локали используют стандартную систему .NET ResX и находятся в `Sources/HyPrism.Desktop/Localization/`:

- `Resources.resx` — английский fallback-ресурс
- `Resources.{тег BCP-47}.resx` — переведённая локаль, например `Resources.ru-RU.resx`

MSBuild автоматически преобразует переводы в satellite assemblies.

Сейчас приложение содержит 13 локалей:

| Код | Нативное название |
|-----|-------------------|
| be-BY | Беларуская |
| de-DE | Deutsch |
| en-US | English |
| es-ES | Español |
| fr-FR | Français |
| it-IT | Italiano |
| ja-JP | 日本語 |
| ko-KR | 한국어 |
| pt-BR | Português (Brasil) |
| ru-RU | Русский |
| tr-TR | Türkçe |
| uk-UA | Українська |
| zh-CN | 简体中文 |

## Ответственность во время выполнения

`Sources/HyPrism.Desktop/Localization/LocalizationService.cs`:

- использует `ResourceManager` для стандартного поиска ресурсов и fallback;
- читает каталог культур из `_supportedCultures` в основном ресурсе;
- читает `_langName` каждой локали для селектора в настройках;
- использует `en-US` для отсутствующих переводов и неподдерживаемых сохранённых тегов;
- преобразует старые короткие теги наподобие `ru` в доступную локаль `ru-RU`;
- устанавливает `CurrentCulture` и `CurrentUICulture`;
- вызывает `LanguageChanged` после переключения языка во время работы.

Страница настроек сохраняет нормализованный тег через `ISettingsService`. Сервис относится к нему как к непрозрачному значению: проверка и применение языка остаются ответственностью Desktop.

## Формат файла

```xml
<data name="_langName" xml:space="preserve">
  <value>Русский</value>
</data>
<data name="button.play" xml:space="preserve">
  <value>Играть</value>
</data>
<data name="dashboard.welcome" xml:space="preserve">
  <value>Добро пожаловать, {0}!</value>
</data>
```

- Имена ресурсов используют ключи с точками, например `dashboard.welcome`
- Подстановки используют составное форматирование .NET: `{0}`, `{1}` и так далее
- `_langName` задаёт нативное название языка в селекторе
- `_supportedCultures` в `Resources.resx` содержит разделённый точками с запятой каталог локалей
- При отсутствии ключа `ResourceManager` использует английский перевод, а затем сам ключ

## Добавление локали

1. Добавьте полный файл `Resources.{тег BCP-47}.resx` в `Sources/HyPrism.Desktop/Localization/`
2. Укажите в `_langName` нативное название языка
3. Добавьте тег в `_supportedCultures` файла `Resources.resx`
4. Сохраните тот же набор имён ресурсов, что и в fallback-файле
5. Запустите `dotnet test Tests/HyPrism.Desktop.Tests/`

Обновлять Core-сервис или регистрацию DI не требуется.
