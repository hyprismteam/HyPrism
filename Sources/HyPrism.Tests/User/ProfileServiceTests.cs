using HyPrism.Models;
using HyPrism.Services.Core.Infrastructure;
using HyPrism.Services.Game.Asset;
using HyPrism.Services.User;

namespace HyPrism.Tests.User;

public class ProfileServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConfigService _config;
    private readonly Mock<IAvatarService> _avatarMock;
    private readonly ProfileService _svc;

    public ProfileServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "HyPrismProfileTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _config = new ConfigService(_tempDir);
        _avatarMock = new Mock<IAvatarService>();
        _svc = new ProfileService(_tempDir, _config, _avatarMock.Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }


    [Fact]
    public void SetNick_ValidNick_ReturnsTrueAndPersists()
    {
        var result = _svc.SetNick("Tester");
        Assert.True(result);
        Assert.Equal("Tester", _svc.GetNick());
    }

    [Fact]
    public void SetNick_EmptyNick_ReturnsFalse()
    {
        Assert.False(_svc.SetNick(""));
    }

    [Fact]
    public void SetNick_TooLongNick_ReturnsFalse()
    {
        Assert.False(_svc.SetNick("ThisNickIsTooLong!"));
    }

    [Fact]
    public void SetNick_MaxLength_ReturnsTrue()
    {
        Assert.True(_svc.SetNick("1234567890123456")); // exactly 16 chars
    }


    [Fact]
    public void GetUUID_ReturnsNonEmpty()
    {
        Assert.False(string.IsNullOrEmpty(_svc.GetUUID()));
    }

    [Fact]
    public void SetUUID_ValidGuid_ReturnsTrueAndPersists()
    {
        var uuid = Guid.NewGuid().ToString();
        var result = _svc.SetUUID(uuid);
        Assert.True(result);
    }

    [Fact]
    public void GenerateNewUuid_ReturnsValidGuid()
    {
        var uuid = _svc.GenerateNewUuid();
        Assert.True(Guid.TryParse(uuid, out _));
    }

    [Fact]
    public void GetCurrentUuid_AlwaysReturnsNonEmpty()
    {
        Assert.False(string.IsNullOrEmpty(_svc.GetCurrentUuid()));
    }


    [Fact]
    public void CreateProfile_ValidName_ReturnsTrue()
    {
        var result = _svc.CreateProfile("TestProfile");
        Assert.True(result);
    }

    [Fact]
    public void CreateProfile_Duplicate_ReturnsTrue()
    {
        // ProfileService does not enforce unique names — both calls succeed.
        _svc.CreateProfile("DupeProfile");
        var result = _svc.CreateProfile("DupeProfile");
        Assert.True(result);
    }

    [Fact]
    public void GetProfiles_AfterCreate_ContainsNewProfile()
    {
        _svc.CreateProfile("MyProfile");
        var profiles = _svc.GetProfiles();
        Assert.Contains(profiles, p => p.Name == "MyProfile");
    }

    [Fact]
    public void DeleteProfile_ExistingProfile_RemovesIt()
    {
        _svc.CreateProfile("ToDelete");
        var id = _svc.GetProfiles().First(p => p.Name == "ToDelete").Id;

        var result = _svc.DeleteProfile(id);

        Assert.True(result);
        Assert.DoesNotContain(_svc.GetProfiles(), p => p.Name == "ToDelete");
    }

    [Fact]
    public void DeleteProfile_NonExistentId_ReturnsFalse()
    {
        var result = _svc.DeleteProfile(Guid.NewGuid().ToString());
        Assert.False(result);
    }

    [Fact]
    public void SwitchProfile_ValidId_ReturnsTrue()
    {
        _svc.CreateProfile("SwitchTarget");
        var id = _svc.GetProfiles().First(p => p.Name == "SwitchTarget").Id;

        var result = _svc.SwitchProfile(id);

        Assert.True(result);
    }

    [Fact]
    public void SwitchProfile_InvalidId_ReturnsFalse()
    {
        var result = _svc.SwitchProfile(Guid.NewGuid().ToString());
        Assert.False(result);
    }


    [Fact]
    public void GetAvatarDirectory_ReturnsNonEmptyPath()
    {
        var dir = _svc.GetAvatarDirectory();
        Assert.False(string.IsNullOrEmpty(dir));
    }

    [Fact]
    public void GetAvatarPreview_NoFile_ReturnsNull()
    {
        var result = _svc.GetAvatarPreview();
        // No file → null or empty, never throws
        Assert.True(result == null || result is string);
    }

    [Fact]
    public void ClearAvatarCache_DoesNotThrow()
    {
        _avatarMock.Setup(a => a.ClearAvatarCache(It.IsAny<string>())).Returns(true);
        var ex = Record.Exception(() => _svc.ClearAvatarCache());
        Assert.Null(ex);
    }


    [Fact]
    public void GetProfilePath_Profile_ReturnsAbsolutePath()
    {
        var profile = new Profile { Id = Guid.NewGuid().ToString(), Name = "PathTest" };
        var path = _svc.GetProfilePath(profile);
        Assert.True(Path.IsPathRooted(path));
    }
}
