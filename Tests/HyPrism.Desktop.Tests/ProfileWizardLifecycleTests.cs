// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using HyPrism.Core.Accounts;
using HyPrism.Core.Models;
using HyPrism.Desktop.Features.Profiles;
using HyPrism.Desktop.Localization;
using HyPrism.Desktop.Platform;
using Moq;
using Xunit;

namespace HyPrism.Desktop.Tests;

public sealed class ProfileWizardLifecycleTests
{
    [AvaloniaFact]
    public async Task CreatingProfileThenOpeningWizardAgainShowsAccountTypeChoice()
    {
        var profiles = new List<Profile>();
        var activeProfileId = string.Empty;
        var profileManager = new Mock<IProfileManager>();
        var profileRepository = new Mock<IProfileRepository>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        profileRepository.Setup(repository => repository.GetProfiles()).Returns(() => profiles.ToList());
        profileRepository.Setup(repository => repository.GetSelectedProfileId()).Returns(() => activeProfileId);
        profileRepository.Setup(repository => repository.CreateProfile(
                "New_Profile",
                It.IsAny<string>(),
                false))
            .Returns((string name, string uuid, bool _) =>
            {
                var profile = new Profile
                {
                    Id = "new-profile",
                    Name = name,
                    UUID = uuid
                };
                profiles.Add(profile);
                return profile;
            });
        profileRepository.Setup(repository => repository.SwitchProfile("new-profile"))
            .Callback(() => activeProfileId = "new-profile")
            .Returns(true);

        using var viewModel = new ProfilesViewModel(
            profileManager.Object,
            profileRepository.Object,
            uriLauncher.Object,
            new StringLocalizer("en-US"));
        var view = new ProfilesView { DataContext = viewModel };
        var window = new Window
        {
            Width = 1180,
            Height = 760,
            Content = view
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        viewModel.ShowCreateChoiceCommand.Execute(null);
        await Task.Delay(420);
        Dispatcher.UIThread.RunJobs();
        view.FindControl<Button>("BeginOfflineProfileCreationButton")!
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(240);
        Dispatcher.UIThread.RunJobs();
        viewModel.OfflineProfileName = "New_Profile";
        viewModel.CreateOfflineProfileCommand.Execute(null);
        await Task.Delay(420);
        Dispatcher.UIThread.RunJobs();

        viewModel.ShowCreateChoiceCommand.Execute(null);
        await Task.Delay(420);
        Dispatcher.UIThread.RunJobs();

        var choice = view.FindControl<StackPanel>("ProfileCreationChoiceContent");
        Assert.NotNull(choice);
        Assert.True(choice!.IsEffectivelyVisible);
        Assert.Equal(1, choice.Opacity);
        Assert.Equal(0, Assert.IsType<TranslateTransform>(choice.RenderTransform).X);
        window.Close();
    }
}
