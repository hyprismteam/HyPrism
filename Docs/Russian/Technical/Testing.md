<!--
Copyright (C) 2026 HyPrism Launcher
SPDX-License-Identifier: GPL-3.0-only
-->

# Руководство по тестированию

Данный документ описывает подход к юнит-тестированию HyPrism: структуру проекта, запуск тестов и соглашения о написании новых тестов.

---

## Структура проекта

```
Tests/HyPrism.Core.Tests/
├── HyPrism.Core.Tests.csproj
├── GlobalUsings.cs
├── Core/
│   ├── Infrastructure/
│   │   ├── ConfigServiceTests.cs
│   │   ├── FileServiceTests.cs
│   │   └── UtilityServiceTests.cs
│   └── App/
│       ├── ProgressNotificationServiceTests.cs
│       ├── SettingsServiceTests.cs
│       └── UpdateServiceTests.cs
├── Game/
│   ├── Auth/
│   │   └── AuthServiceTests.cs
│   ├── Launch/
│   │   ├── ClientPatcherTests.cs
│   │   └── JvmArgumentBuilderTests.cs
│   └── Sources/
│       └── MirrorDiscoveryServiceTests.cs
└── User/
    ├── ProfileManagementServiceTests.cs
    └── ProfileServiceTests.cs
```

Headless- и render-тесты Avalonia находятся рядом в `Tests/HyPrism.Desktop.Tests/`. `LocalizationServiceTests.cs` проверяет ResX-каталог, fallback, смену культуры и runtime-уведомления в presentation-слое.

---

## Используемый стек

| Библиотека | Назначение |
|------------|------------|
| **xUnit 2.9** | Фреймворк для тестов и ассертов |
| **Moq 4.20** | Создание моков интерфейсов и абстрактных классов |
| **coverlet** | Сбор данных о покрытии кода |
| **Microsoft.NET.Test.Sdk** | Интеграция с VS/CLI |

---

## Запуск тестов

```bash
# Запустить все тесты
dotnet test Tests/HyPrism.Core.Tests/

# Запустить с подробным выводом
dotnet test Tests/HyPrism.Core.Tests/ --logger "console;verbosity=detailed"

# Запустить конкретный класс тестов
dotnet test Tests/HyPrism.Core.Tests/ --filter "FullyQualifiedName~UtilityServiceTests"

# Запустить со сбором покрытия кода
dotnet test Tests/HyPrism.Core.Tests/ --collect:"XPlat Code Coverage"
```

---

## Покрытие интерфейсами

Каждый инжектируемый сервис обязан реализовывать интерфейс. Ниже приведена полная таблица соответствий.

### Core — Infrastructure

| Сервис | Интерфейс |
|--------|-----------|
| `ConfigService` | `IConfigService` |
| `FileService` | `IFileService` |

### Core — App

| Сервис | Интерфейс |
|--------|-----------|
| `ProgressNotificationService` | `IProgressNotificationService` |
| `SettingsService` | `ISettingsService` |
| `ThemeService` | `IThemeService` |
| `UpdateService` | `IUpdateService` |

### Core — Integration

| Сервис | Интерфейс |
|--------|-----------|
| `DiscordService` | `IDiscordService` |
| `GitHubService` | `IGitHubService` |
| `NewsService` | `INewsService` |

### Core — Platform

| Сервис | Интерфейс |
|--------|-----------|
| `BrowserService` | `IBrowserService` |
| Electron `ClipboardService` (адаптер legacy-host) | `IClipboardService` (контракт Core) |
| `FileDialogService` | `IFileDialogService` |
| `GpuDetectionService` | `IGpuDetectionService` |
| `RosettaService` | `IRosettaService` |

### Game

| Сервис | Интерфейс |
|--------|-----------|
| `AuthService` | `IAuthService` |
| `AvatarService` | `IAvatarService` |
| `AssetService` | `IAssetService` |
| `ButlerService` | `IButlerService` |
| `ClientPatcher` | `IClientPatcher` |
| `DownloadService` | `IDownloadService` |
| `GameLauncher` | `IGameLauncher` |
| `GameProcessService` | `IGameProcessService` |
| `GameSessionService` | `IGameSessionService` |
| `InstanceMigrationService` | `IInstanceMigrationService` |
| `InstanceService` | `IInstanceService` |
| `LaunchService` | `ILaunchService` |
| `MirrorDiscoveryService` | `IMirrorDiscoveryService` |
| `ModService` | `IModService` |
| `PatchManager` | `IPatchManager` |
| `VersionService` | `IVersionService` |

### User

| Сервис | Интерфейс |
|--------|-----------|
| `HytaleAuthService` | `IHytaleAuthService` |
| `ProfileManagementService` | `IProfileManagementService` |
| `ProfileService` | `IProfileService` |
| `SkinService` | `ISkinService` |
| `UserIdentityService` | `IUserIdentityService` |

### Статические вспомогательные классы (интерфейс не нужен)

`UtilityService`, `SystemInfoService`, `DualAuthService`, `MirrorLoaderService`,
`JvmArgumentBuilder`, `LauncherPackageExtractor`, `ProfileMigrationService`, `TokenStore`

---

## Написание новых тестов

### Именование файлов

Зеркально повторяет пространство имён продакшн-кода:

```
Sources/HyPrism.Core/Game/Auth/AuthService.cs
  ↓
Tests/HyPrism.Core.Tests/Game/Auth/AuthServiceTests.cs
```

### Структура класса

```csharp
// Tests/HyPrism.Core.Tests/Game/Example/MyServiceTests.cs
using HyPrism.Services.Game.Example;

namespace HyPrism.Core.Tests.Game.Example;

public class MyServiceTests : IDisposable
{
    // 1. Общее состояние инициализируется в конструкторе
    private readonly Mock<IDependency> _dep = new();
    private readonly MyService _svc;

    public MyServiceTests()
    {
        _dep.Setup(d => d.Method()).Returns(42);
        _svc = new MyService(_dep.Object);
    }

    // 2. Очистка временных ресурсов
    public void Dispose() { /* ... */ }

    // 3. Один [Fact] или [Theory] на одно поведение
    [Fact]
    public void Method_Condition_ExpectedOutcome()
    {
        var result = _svc.Method();
        Assert.Equal(42, result);
    }
}
```

### Мокирование HTTP-запросов

Сервисы, использующие `HttpClient`, тестируются через подменённый `HttpMessageHandler`:

```csharp
private static HttpClient BuildClient(HttpStatusCode status, string body)
{
    var handler = new StubHttpHandler(status, body);
    return new HttpClient(handler);
}

private sealed class StubHttpHandler(HttpStatusCode status, string body) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
    }
}
```

### Изоляция файловой системы

Используйте отдельный временный каталог на класс тестов; удаляйте его в `Dispose`:

```csharp
private readonly string _tempDir =
    Path.Combine(Path.GetTempPath(), "HyPrismTests_" + Guid.NewGuid());

public MyTests() => Directory.CreateDirectory(_tempDir);
public void Dispose() => Directory.Delete(_tempDir, true);
```

---

## Соглашение об именовании тестов

```
ИмяМетода_Условие_ОжидаемыйРезультат
```

Примеры:

- `SetNick_EmptyNick_ReturnsFalse`
- `GetGameSessionTokenAsync_SuccessResponse_ReturnsToken`
- `CopyDirectory_NonExistentSource_DoesNotThrow`

---

## Покрытие кода

После запуска тестов с флагом `--collect:"XPlat Code Coverage"` в каталоге `TestResults/` появится файл `coverage.cobertura.xml`. HTML-отчёт генерируется через [ReportGenerator](https://github.com/danielpalme/ReportGenerator):

```bash
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:"**/coverage.cobertura.xml" -targetdir:"coveragereport" -reporttypes:Html
```
