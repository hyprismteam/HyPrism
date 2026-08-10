<!--
Copyright (C) 2026 HyPrism Launcher
SPDX-License-Identifier: GPL-3.0-only
-->

# Localization

The Avalonia desktop application owns UI localization. `HyPrism.Core` only persists the selected language tag in `Config.Language`; it does not load translations, maintain a locale catalog, or change the process culture.

## Locale Files

Locale sources use the standard .NET ResX system and live under `Sources/HyPrism.Desktop/Localization/`:

- `Resources.resx` is the English fallback resource
- `Resources.{BCP-47 tag}.resx` contains a translated locale, for example `Resources.ru-RU.resx`

MSBuild turns translated resources into satellite assemblies automatically.

The application currently ships 13 locales:

| Code | Native name |
|------|-------------|
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

## Runtime Ownership

`Sources/HyPrism.Desktop/Localization/LocalizationService.cs`:

- uses `ResourceManager` for standard .NET resource lookup and fallback;
- reads the culture catalog from `_supportedCultures` in the fallback resource;
- reads each locale's `_langName` for the settings selector;
- falls back to `en-US` for missing translations or unsupported saved tags;
- maps legacy short tags such as `ru` to an embedded locale such as `ru-RU`;
- applies `CurrentCulture` and `CurrentUICulture`;
- raises `LanguageChanged` after a runtime switch.

The settings view persists the normalized tag through `ISettingsService`. The service treats it as an opaque preference: validation and application remain Desktop responsibilities.

## File Format

```xml
<data name="_langName" xml:space="preserve">
  <value>English</value>
</data>
<data name="button.play" xml:space="preserve">
  <value>Play</value>
</data>
<data name="dashboard.welcome" xml:space="preserve">
  <value>Welcome, {0}!</value>
</data>
```

- Resource names use dotted keys such as `dashboard.welcome`
- Placeholders use .NET composite formatting: `{0}`, `{1}`, and so on
- `_langName` is the native language name displayed by the language selector
- `_supportedCultures` in `Resources.resx` contains the semicolon-separated locale catalog
- Missing keys fall back through `ResourceManager` to English and then to the key itself

## Adding a Locale

1. Add a complete `Resources.{BCP-47 tag}.resx` file under `Sources/HyPrism.Desktop/Localization/`
2. Set `_langName` to the language's native display name
3. Add the tag to `_supportedCultures` in `Resources.resx`
4. Keep the same resource names as the fallback file
5. Run `dotnet test Tests/HyPrism.Desktop.Tests/`

No Core service or DI registration needs to be updated.
