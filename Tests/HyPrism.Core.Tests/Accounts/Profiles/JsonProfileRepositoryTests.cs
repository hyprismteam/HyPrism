// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core.Models;
using HyPrism.Core.Infrastructure;
using HyPrism.Core.Game.Instances;
using HyPrism.Core.Accounts;
using System.Text.Json;

namespace HyPrism.Core.Tests.Accounts.Profiles;

public class JsonProfileRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly JsonConfigStore _config;
    private readonly Mock<ISkinRepository> _skinMock;
    private readonly Mock<IInstanceRepository> _instanceMock;
    private readonly Mock<IUserIdentityProvider> _identityMock;
    private readonly JsonProfileRepository _svc;

    public JsonProfileRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "HyPrismPMTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _config = new JsonConfigStore(_tempDir);
        _skinMock = new Mock<ISkinRepository>();
        _instanceMock = new Mock<IInstanceRepository>();
        _identityMock = new Mock<IUserIdentityProvider>();

        _instanceMock.Setup(i => i.GetInstanceRoot()).Returns(_tempDir);
        _instanceMock.Setup(i => i.GetInstanceRootsIncludingLegacy()).Returns([_tempDir]);

        _svc = new JsonProfileRepository(
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
    public void CreateProfile_OfficialProfile_PersistsIsOfficialInProfilesJson()
    {
        var uuid = Guid.NewGuid().ToString();
        var profile = _svc.CreateProfile("OfficialUser", uuid, isOfficial: true);

        Assert.NotNull(profile);
        Assert.True(profile!.IsOfficial);
        Assert.Contains(_svc.GetProfiles(), p => p.Id == profile.Id && p.IsOfficial);

        var profilesPath = Path.Combine(_svc.GetProfilesFolder(), "profiles.json");
        var savedProfiles = JsonSerializer.Deserialize<List<Profile>>(File.ReadAllText(profilesPath));

        Assert.NotNull(savedProfiles);
        Assert.Contains(savedProfiles!, p => p.Id == profile.Id && p.IsOfficial);
    }

    [Fact]
    public void CreateProfile_DuplicateName_ReturnsProfile()
    {
        // JsonProfileRepository does not enforce unique names, so both calls succeed
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
    public void GetCurrentProfileFolder_ReturnsPreparedFolderWithoutOpeningIt()
    {
        var profile = _svc.CreateProfile("FolderOwner", Guid.NewGuid().ToString())!;
        var expectedPath = Path.Combine(_svc.GetProfilesFolder(), profile.Id);
        if (Directory.Exists(expectedPath))
            Directory.Delete(expectedPath, true);

        var path = _svc.GetCurrentProfileFolder();

        Assert.Equal(expectedPath, path);
        Assert.True(Directory.Exists(expectedPath));
        Assert.True(File.Exists(Path.Combine(expectedPath, "profile.json")));
    }


    [Fact]
    public void GetSelectedProfileId_Initially_ReturnsStringOrEmpty()
    {
        var id = _svc.GetSelectedProfileId();
        Assert.NotNull(id);
    }

}
