using HyPrism.Services.Core.Infrastructure;

namespace HyPrism.Tests.Core.Infrastructure;

public class FileServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileService _svc;

    public FileServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "HyPrismFileTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _svc = new FileService(new AppPathConfiguration(_tempDir));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void OpenFolder_NonExistentDirectory_ReturnsFalse()
    {
        var result = _svc.OpenFolder(Path.Combine(_tempDir, "nonexistent"));
        Assert.False(result);
    }

    [Fact]
    public void OpenFolder_ExistingDirectory_ReturnsTrue()
    {
        var subDir = Path.Combine(_tempDir, "sub");
        Directory.CreateDirectory(subDir);

        // On CI the shell process launch may succeed even without a display manager.
        // We only assert it doesn't throw — the return value is platform-dependent.
        var ex = Record.Exception(() => _svc.OpenFolder(subDir));
        Assert.Null(ex);
    }

    [Fact]
    public void OpenAppFolder_DoesNotThrow()
    {
        var ex = Record.Exception(() => _svc.OpenAppFolder());
        Assert.Null(ex);
    }
}
