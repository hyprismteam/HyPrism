// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core.Accounts;
using HyPrism.Core.Models;
using HyPrism.Desktop.Features.Profiles;
using HyPrism.Desktop.Localization;
using HyPrism.Desktop.Platform;
using Moq;
using Xunit;

namespace HyPrism.Desktop.Tests;

public sealed class ProfilesViewModelTests
{
    [Fact]
    public void CreateOfflineProfile_ActivatesAndSelectsNewProfile()
    {
        var profiles = new List<Profile>
        {
            new()
            {
                Id = "existing",
                Name = "Existing",
                UUID = Guid.NewGuid().ToString()
            }
        };
        var activeProfileId = "existing";
        var profileManager = new Mock<IProfileManager>();
        var profileRepository = new Mock<IProfileRepository>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        var profileChanged = 0;

        profileRepository.Setup(repository => repository.GetProfiles()).Returns(() => profiles.ToList());
        profileRepository.Setup(repository => repository.GetSelectedProfileId()).Returns(() => activeProfileId);
        profileRepository.Setup(repository => repository.CreateProfile(
                "Offline_Player",
                It.IsAny<string>(),
                false))
            .Returns((string name, string uuid, bool _) =>
            {
                var profile = new Profile
                {
                    Id = "offline-player",
                    Name = name,
                    UUID = uuid
                };
                profiles.Add(profile);
                return profile;
            });
        profileRepository.Setup(repository => repository.SwitchProfile(It.IsAny<string>()))
            .Callback<string>(id => activeProfileId = id)
            .Returns(true);

        using var viewModel = new ProfilesViewModel(
            profileManager.Object,
            profileRepository.Object,
            uriLauncher.Object,
            new StringLocalizer("en-US"));
        viewModel.ActiveProfileChanged += (_, _) => profileChanged++;

        viewModel.ShowCreateChoiceCommand.Execute(null);
        viewModel.BeginOfflineCreationCommand.Execute(null);
        viewModel.OfflineProfileName = "Offline_Player";
        viewModel.CreateOfflineProfileCommand.Execute(null);

        profileRepository.Verify(repository => repository.CreateProfile(
            "Offline_Player",
            It.Is<string>(uuid => IsGuid(uuid)),
            false), Times.Once);
        profileRepository.Verify(repository => repository.SwitchProfile("offline-player"), Times.Once);
        Assert.Equal("offline-player", viewModel.SelectedProfile?.Id);
        Assert.True(viewModel.SelectedProfile?.IsActive);
        Assert.Equal(1, profileChanged);
    }

    [Fact]
    public void MoveProfile_ReordersCardsAndPersistsOrder()
    {
        var profiles = new List<Profile>
        {
            new() { Id = "first", Name = "First", UUID = Guid.NewGuid().ToString() },
            new() { Id = "second", Name = "Second", UUID = Guid.NewGuid().ToString() },
            new() { Id = "third", Name = "Third", UUID = Guid.NewGuid().ToString() }
        };
        var profileManager = new Mock<IProfileManager>();
        var profileRepository = new Mock<IProfileRepository>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        profileRepository.Setup(repository => repository.GetProfiles()).Returns(profiles);
        profileRepository.Setup(repository => repository.GetSelectedProfileId()).Returns("first");

        using var viewModel = new ProfilesViewModel(
            profileManager.Object,
            profileRepository.Object,
            uriLauncher.Object,
            new StringLocalizer("en-US"));

        viewModel.MoveProfile("third", 0);

        Assert.Equal(["third", "first", "second"], viewModel.Profiles.Select(profile => profile.Id));
        Assert.Equal("first", viewModel.SelectedProfile?.Id);
        profileRepository.Verify(
            repository => repository.SetProfileOrder(
                It.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(new[] { "third", "first", "second" }))),
            Times.Once);
    }

    [Fact]
    public void SelectProfile_OnlyOpensDetails_UntilActivationIsRequested()
    {
        var profiles = new List<Profile>
        {
            new() { Id = "active", Name = "Active", UUID = Guid.NewGuid().ToString() },
            new() { Id = "preview", Name = "Preview", UUID = Guid.NewGuid().ToString() }
        };
        var activeProfileId = "active";
        var profileManager = new Mock<IProfileManager>();
        var profileRepository = new Mock<IProfileRepository>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        var profileChanged = 0;
        profileRepository.Setup(repository => repository.GetProfiles()).Returns(() => profiles.ToList());
        profileRepository.Setup(repository => repository.GetSelectedProfileId()).Returns(() => activeProfileId);
        profileRepository.Setup(repository => repository.SwitchProfile("preview"))
            .Callback(() => activeProfileId = "preview")
            .Returns(true);

        using var viewModel = new ProfilesViewModel(
            profileManager.Object,
            profileRepository.Object,
            uriLauncher.Object,
            new StringLocalizer("en-US"));
        viewModel.ActiveProfileChanged += (_, _) => profileChanged++;
        var preview = viewModel.Profiles.Single(profile => profile.Id == "preview");

        viewModel.SelectProfileCommand.Execute(preview);

        Assert.Equal("preview", viewModel.SelectedProfile?.Id);
        Assert.True(viewModel.SelectedProfile?.IsSelected);
        Assert.False(viewModel.SelectedProfile?.IsActive);
        Assert.True(viewModel.CanActivateSelectedProfile);
        profileRepository.Verify(repository => repository.SwitchProfile(It.IsAny<string>()), Times.Never);

        viewModel.ActivateSelectedProfileCommand.Execute(null);

        profileRepository.Verify(repository => repository.SwitchProfile("preview"), Times.Once);
        Assert.Equal("preview", viewModel.SelectedProfile?.Id);
        Assert.True(viewModel.SelectedProfile?.IsActive);
        Assert.False(viewModel.CanActivateSelectedProfile);
        Assert.Equal(1, profileChanged);
    }

    [Fact]
    public void RequestProfileDeletion_RejectsSelectedLauncherProfile()
    {
        var profiles = new List<Profile>
        {
            new() { Id = "active", Name = "Active", UUID = Guid.NewGuid().ToString() },
            new() { Id = "inactive", Name = "Inactive", UUID = Guid.NewGuid().ToString() }
        };
        var profileManager = new Mock<IProfileManager>();
        var profileRepository = new Mock<IProfileRepository>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        profileRepository.Setup(repository => repository.GetProfiles()).Returns(profiles);
        profileRepository.Setup(repository => repository.GetSelectedProfileId()).Returns("active");

        using var viewModel = new ProfilesViewModel(
            profileManager.Object,
            profileRepository.Object,
            uriLauncher.Object,
            new StringLocalizer("en-US"));

        viewModel.RequestProfileDeletionCommand.Execute(
            viewModel.Profiles.Single(profile => profile.Id == "active"));

        Assert.Null(viewModel.PendingProfileDeletion);

        var inactive = viewModel.Profiles.Single(profile => profile.Id == "inactive");
        viewModel.RequestProfileDeletionCommand.Execute(inactive);

        Assert.Same(inactive, viewModel.PendingProfileDeletion);
    }

    [Fact]
    public async Task OpenProfileFolder_UsesDisplayedProfileInsteadOfActiveProfile()
    {
        var profiles = new List<Profile>
        {
            new() { Id = "active", Name = "Active", UUID = Guid.NewGuid().ToString() },
            new() { Id = "preview", Name = "Preview", UUID = Guid.NewGuid().ToString() }
        };
        var profileManager = new Mock<IProfileManager>();
        var profileRepository = new Mock<IProfileRepository>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        profileRepository.Setup(repository => repository.GetProfiles()).Returns(() => profiles.ToList());
        profileRepository.Setup(repository => repository.GetSelectedProfileId()).Returns("active");
        profileManager.Setup(manager => manager.GetProfilePath(
                It.Is<Profile>(profile => profile.Id == "preview")))
            .Returns("/tmp/hyprism-preview-profile");
        uriLauncher.Setup(launcher => launcher.LaunchDirectoryAsync("/tmp/hyprism-preview-profile"))
            .ReturnsAsync(true);

        using var viewModel = new ProfilesViewModel(
            profileManager.Object,
            profileRepository.Object,
            uriLauncher.Object,
            new StringLocalizer("en-US"));
        viewModel.SelectProfileCommand.Execute(viewModel.Profiles.Single(profile => profile.Id == "preview"));

        await viewModel.OpenProfileFolderCommand.ExecuteAsync(null);

        profileManager.Verify(manager => manager.GetProfilePath(
            It.Is<Profile>(profile => profile.Id == "preview")), Times.Once);
        uriLauncher.Verify(
            launcher => launcher.LaunchDirectoryAsync("/tmp/hyprism-preview-profile"),
            Times.Once);
    }

    private static bool IsGuid(string value)
        => Guid.TryParse(value, out _);
}
