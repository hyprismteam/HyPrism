using HyPrism.Models;
using HyPrism.Services.Core.Infrastructure;
using HyPrism.Services.Game.Instance;
using HyPrism.Services.User;

namespace HyPrism.Tests.User;

public class ProfileManagementServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConfigService _config;
    private readonly Mock<ISkinService> _skinMock;
    private readonly Mock<IInstanceService> _instanceMock;
    private readonly Mock<IUserIdentityService> _identityMock;
    private readonly ProfileManagementService _svc;

    public ProfileManagementServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "HyPrismPMTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _config = new ConfigService(_tempDir);
        _skinMock = new Mock<ISkinService>();
        _instanceMock = new Mock<IInstanceService>();
        _identityMock = new Mock<IUserIdentityService>();

        _instanceMock.Setup(i => i.GetInstanceRoot()).Returns(_tempDir);
        _instanceMock.Setup(i => i.GetInstanceRootsIncludingLegacy()).Returns([_tempDir]);

        _svc = new ProfileManagementService(
            new AppPathConfiguration(_tempDir),
            _config,
            _skinMock.Object,
            _instanceMock.Object,
            _identityMock.Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }


    [Fact]
    public void GetProfiles_Initially_ReturnsEmptyOrDefaultList()
    {
        var profiles = _svc.GetProfiles();
        Assert.NotNull(profiles);
    }


    [Fact]
    public void CreateProfile_ValidArgs_ReturnsProfile()
    {
        var uuid = Guid.NewGuid().ToString();
        var profile = _svc.CreateProfile("TestUser", uuid);

        Assert.NotNull(profile);
        Assert.Equal("TestUser", profile!.Name);
    }

    [Fact]
    public void CreateProfile_AfterCreate_AppearsInGetProfiles()
    {
        var uuid = Guid.NewGuid().ToString();
        _svc.CreateProfile("Visible", uuid);

        var profiles = _svc.GetProfiles();
        Assert.Contains(profiles, p => p.Name == "Visible");
    }

    [Fact]
    public void CreateProfile_DuplicateName_ReturnsProfile()
    {
        // ProfileManagementService does not enforce unique names — both calls succeed.
        var uuid = Guid.NewGuid().ToString();
        _svc.CreateProfile("Dupe", uuid);

        var second = _svc.CreateProfile("Dupe", Guid.NewGuid().ToString());
        Assert.NotNull(second);
    }


    [Fact]
    public void DeleteProfile_ExistingProfile_ReturnsTrueAndRemoves()
    {
        var uuid = Guid.NewGuid().ToString();
        var profile = _svc.CreateProfile("ToDelete", uuid)!;

        var result = _svc.DeleteProfile(profile.Id);

        Assert.True(result);
        Assert.DoesNotContain(_svc.GetProfiles(), p => p.Id == profile.Id);
    }

    [Fact]
    public void DeleteProfile_NonExistent_ReturnsFalse()
    {
        var result = _svc.DeleteProfile(Guid.NewGuid().ToString());
        Assert.False(result);
    }


    [Fact]
    public void SwitchProfile_ById_ValidId_ReturnsTrue()
    {
        var uuid = Guid.NewGuid().ToString();
        _identityMock.Setup(i => i.GetUuidForUser(It.IsAny<string>())).Returns(uuid);

        var profile = _svc.CreateProfile("Switchable", uuid)!;
        var result = _svc.SwitchProfile(profile.Id);

        Assert.True(result);
    }

    [Fact]
    public void SwitchProfile_ById_InvalidId_ReturnsFalse()
    {
        var result = _svc.SwitchProfile(Guid.NewGuid().ToString());
        Assert.False(result);
    }

    [Fact]
    public void SwitchProfile_ByIndex_OutOfRange_ReturnsFalse()
    {
        var result = _svc.SwitchProfile(999);
        Assert.False(result);
    }


    [Fact]
    public void UpdateProfile_ValidId_ReturnsTrue()
    {
        var uuid = Guid.NewGuid().ToString();
        var profile = _svc.CreateProfile("OldName", uuid)!;

        var result = _svc.UpdateProfile(profile.Id, "NewName", null);

        Assert.True(result);
        Assert.Contains(_svc.GetProfiles(), p => p.Name == "NewName");
    }

    [Fact]
    public void UpdateProfile_NonExistentId_ReturnsFalse()
    {
        var result = _svc.UpdateProfile(Guid.NewGuid().ToString(), "Name", null);
        Assert.False(result);
    }


    [Fact]
    public void GetProfilesFolder_ReturnsAbsolutePath()
    {
        var folder = _svc.GetProfilesFolder();
        Assert.True(Path.IsPathRooted(folder));
    }


    [Fact]
    public void GetSelectedProfileId_Initially_ReturnsStringOrEmpty()
    {
        var id = _svc.GetSelectedProfileId();
        Assert.NotNull(id);
    }

    [Fact]
    public void GetActiveProfileIndex_Initially_ReturnsValidIndex()
    {
        var idx = _svc.GetActiveProfileIndex();
        Assert.True(idx >= -1);
    }
}
