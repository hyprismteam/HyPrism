# Руководство по тестированию

Данный документ описывает подход к юнит-тестированию HyPrism: структуру проекта, запуск тестов и соглашения о написании новых тестов.

---

## Структура проекта

```
HyPrism.Tests/
├── HyPrism.Tests.csproj
├── GlobalUsings.cs
├── Core/
│   ├── Infrastructure/
│   │   ├── ConfigServiceTests.cs
│   │   ├── FileServiceTests.cs
│   │   └── UtilityServiceTests.cs
│   └── App/
│       ├── LocalizationServiceTests.cs
│       ├── ProgressNotificationServiceTests.cs
│       └── SettingsServiceTests.cs
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
dotnet test HyPrism.Tests/

# Запустить с подробным выводом
dotnet test HyPrism.Tests/ --logger "console;verbosity=detailed"

# Запустить конкретный класс тестов
dotnet test HyPrism.Tests/ --filter "FullyQualifiedName~UtilityServiceTests"

# Запустить со сбором покрытия кода
dotnet test HyPrism.Tests/ --collect:"XPlat Code Coverage"
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
| `LocalizationService` | `ILocalizationService` |
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
| `ClipboardService` | `IClipboardService` |
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
Services/Game/Auth/AuthService.cs
  ↓
HyPrism.Tests/Game/Auth/AuthServiceTests.cs
```

### Структура класса

```csharp
// HyPrism.Tests/Game/Example/MyServiceTests.cs
using HyPrism.Services.Game.Example;

namespace HyPrism.Tests.Game.Example;

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
