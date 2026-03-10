# Testing Guide

This document describes the unit testing approach for HyPrism, including project structure, how to run tests, and conventions for writing new tests.

---

## Project Layout

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

## Stack

| Library | Purpose |
|---------|---------|
| **xUnit 2.9** | Test runner and assertions |
| **Moq 4.20** | Mocking interfaces and abstract classes |
| **coverlet** | Code coverage collection |
| **Microsoft.NET.Test.Sdk** | VS/CLI test host integration |

---

## Running Tests

```bash
# Run all tests
dotnet test HyPrism.Tests/

# Run with verbose output
dotnet test HyPrism.Tests/ --logger "console;verbosity=detailed"

# Run a specific test class
dotnet test HyPrism.Tests/ --filter "FullyQualifiedName~UtilityServiceTests"

# Run with code coverage
dotnet test HyPrism.Tests/ --collect:"XPlat Code Coverage"
```

---

## Service Interface Coverage

All injectable services expose an interface. The table below lists every service and its interface.

### Core — Infrastructure

| Service | Interface |
|---------|-----------|
| `ConfigService` | `IConfigService` |
| `FileService` | `IFileService` |

### Core — App

| Service | Interface |
|---------|-----------|
| `LocalizationService` | `ILocalizationService` |
| `ProgressNotificationService` | `IProgressNotificationService` |
| `SettingsService` | `ISettingsService` |
| `ThemeService` | `IThemeService` |
| `UpdateService` | `IUpdateService` |

### Core — Integration

| Service | Interface |
|---------|-----------|
| `DiscordService` | `IDiscordService` |
| `GitHubService` | `IGitHubService` |
| `NewsService` | `INewsService` |

### Core — Platform

| Service | Interface |
|---------|-----------|
| `BrowserService` | `IBrowserService` |
| `ClipboardService` | `IClipboardService` |
| `FileDialogService` | `IFileDialogService` |
| `GpuDetectionService` | `IGpuDetectionService` |
| `RosettaService` | `IRosettaService` |

### Game

| Service | Interface |
|---------|-----------|
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

| Service | Interface |
|---------|-----------|
| `HytaleAuthService` | `IHytaleAuthService` |
| `ProfileManagementService` | `IProfileManagementService` |
| `ProfileService` | `IProfileService` |
| `SkinService` | `ISkinService` |
| `UserIdentityService` | `IUserIdentityService` |

### Static helpers (no interface needed)

`UtilityService`, `SystemInfoService`, `DualAuthService`, `MirrorLoaderService`,
`JvmArgumentBuilder`, `LauncherPackageExtractor`, `ProfileMigrationService`, `TokenStore`

---

## Writing New Tests

### File naming

Mirror the production namespace, e.g.:
```
Services/Game/Auth/AuthService.cs
  ↓
HyPrism.Tests/Game/Auth/AuthServiceTests.cs
```

### Class structure

```csharp
// HyPrism.Tests/Game/Example/MyServiceTests.cs
using HyPrism.Services.Game.Example;

namespace HyPrism.Tests.Game.Example;

public class MyServiceTests : IDisposable
{
    // 1. Arrange shared state in the constructor
    private readonly Mock<IDependency> _dep = new();
    private readonly MyService _svc;

    public MyServiceTests()
    {
        _dep.Setup(d => d.Method()).Returns(42);
        _svc = new MyService(_dep.Object);
    }

    // 2. Clean up temp files / resources
    public void Dispose() { /* ... */ }

    // 3. One [Fact] or [Theory] per behaviour
    [Fact]
    public void Method_Condition_ExpectedOutcome()
    {
        var result = _svc.Method();
        Assert.Equal(42, result);
    }
}
```

### Mocking HTTP calls

Services that use `HttpClient` are tested with a stub `HttpMessageHandler`:

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

### Isolated filesystem tests

Use a temp directory per test class; clean it up in `Dispose`:

```csharp
private readonly string _tempDir =
    Path.Combine(Path.GetTempPath(), "HyPrismTests_" + Guid.NewGuid());

public MyTests() => Directory.CreateDirectory(_tempDir);
public void Dispose() => Directory.Delete(_tempDir, true);
```

---

## Test Naming Convention

```
MethodName_Condition_ExpectedOutcome
```

Examples:

- `SetNick_EmptyNick_ReturnsFalse`
- `GetGameSessionTokenAsync_SuccessResponse_ReturnsToken`
- `CopyDirectory_NonExistentSource_DoesNotThrow`

---

## Code Coverage

After running tests with `--collect:"XPlat Code Coverage"`, a `coverage.cobertura.xml` file is generated in the `TestResults/` directory. Use [ReportGenerator](https://github.com/danielpalme/ReportGenerator) to produce an HTML report:

```bash
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:"**/coverage.cobertura.xml" -targetdir:"coveragereport" -reporttypes:Html
```
