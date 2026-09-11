// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core.Accounts;
using HyPrism.Core.Models;
using HyPrism.Desktop.Features.Settings;
using HyPrism.Desktop.Localization;
using HyPrism.Desktop.Platform;
using Avalonia.Headless.XUnit;
using Moq;
using System.Net;
using System.Net.Http;
using System.Text;
using Xunit;

namespace HyPrism.Desktop.Tests;

public sealed class NetworkSettingsViewModelTests
{
    [AvaloniaFact]
    public void AuthServerIsHiddenForOfficialProfile()
    {
        var viewModel = CreateViewModel(
            onlineMode: true,
            profile: new Profile { IsOfficial = true });

        Assert.False(viewModel.IsAuthServerVisible);
    }

    [AvaloniaFact]
    public void AuthServerIsHiddenWhenConnectedModeIsDisabled()
    {
        var viewModel = CreateViewModel(
            onlineMode: false,
            profile: new Profile { IsOfficial = false });

        Assert.False(viewModel.IsAuthServerVisible);
    }

    [AvaloniaFact]
    public async Task AuthServersCanBeAddedSelectedAndRemoved()
    {
        var settings = CreateSettingsStore();
        var viewModel = CreateViewModel(
            settings,
            onlineMode: true,
            profile: new Profile { IsOfficial = false },
            httpClient: new HttpClient(new AuthServerHandler(HttpStatusCode.OK)));

        var defaultServer = Assert.Single(viewModel.AuthServerItems);
        Assert.True(defaultServer.IsBuiltIn);
        Assert.True(defaultServer.IsSelected);

        viewModel.ShowAddAuthServerCommand.Execute(null);
        viewModel.NewAuthServer = "https://auth.example.test/";
        await viewModel.AddAuthServerCommand.ExecuteAsync(null);

        var customServer = Assert.Single(viewModel.AuthServerItems.Skip(1));
        Assert.Equal("https://auth.example.test", customServer.Value);
        Assert.True(customServer.IsSelected);
        Assert.Equal(customServer.Value, viewModel.AuthDomain);
        Assert.Equal(customServer.Value, settings.Object.AuthDomain);
        Assert.Contains(customServer.Value, settings.Object.AuthServers);

        viewModel.RemoveAuthServerCommand.Execute(customServer);

        Assert.Single(viewModel.AuthServerItems);
        Assert.True(viewModel.AuthServerItems[0].IsSelected);
        Assert.Equal("sessions.sanasol.ws", settings.Object.AuthDomain);
        Assert.Empty(settings.Object.AuthServers);
    }

    [AvaloniaFact]
    public async Task UnreachableAuthServerIsNotAdded()
    {
        var settings = CreateSettingsStore();
        var viewModel = CreateViewModel(
            settings,
            onlineMode: true,
            profile: new Profile { IsOfficial = false },
            httpClient: new HttpClient(new AuthServerHandler(HttpStatusCode.ServiceUnavailable)));

        viewModel.ShowAddAuthServerCommand.Execute(null);
        viewModel.NewAuthServer = "auth.example.test";
        await viewModel.AddAuthServerCommand.ExecuteAsync(null);

        Assert.Single(viewModel.AuthServerItems);
        Assert.Contains("unreachable", viewModel.AuthServerAddStatus, StringComparison.OrdinalIgnoreCase);
        Assert.True(viewModel.IsAuthServerAddError);
        Assert.Empty(settings.Object.AuthServers);

        viewModel.NewAuthServer = "auth.example.test/";

        Assert.False(viewModel.IsAuthServerAddError);
        Assert.Equal(string.Empty, viewModel.AuthServerAddStatus);
    }

    [AvaloniaFact]
    public async Task OrdinaryWebsiteIsNotAcceptedAsAuthServer()
    {
        var settings = CreateSettingsStore();
        var viewModel = CreateViewModel(
            settings,
            onlineMode: true,
            profile: new Profile { IsOfficial = false },
            httpClient: new HttpClient(new AuthServerHandler(
                HttpStatusCode.OK,
                "{\"message\":\"ordinary website\"}")));

        viewModel.ShowAddAuthServerCommand.Execute(null);
        viewModel.NewAuthServer = "ordinary.example.test";
        await viewModel.AddAuthServerCommand.ExecuteAsync(null);

        Assert.Single(viewModel.AuthServerItems);
        Assert.Empty(settings.Object.AuthServers);
    }

    [AvaloniaFact]
    public async Task AuthServerRowsReportAvailability()
    {
        using var viewModel = CreateViewModel(
            onlineMode: true,
            profile: new Profile { IsOfficial = false },
            httpClient: new HttpClient(new AuthServerHandler(HttpStatusCode.OK)));

        viewModel.SelectCategoryCommand.Execute(
            viewModel.Categories.Single(category => category.Id == "network"));

        var server = Assert.Single(viewModel.AuthServerItems);
        var timeout = DateTime.UtcNow.AddSeconds(1);
        while (server.IsChecking && DateTime.UtcNow < timeout)
            await Task.Delay(10);

        Assert.True(server.IsAvailable);
        Assert.NotEqual("—", server.Ping);
    }

    private static SettingsViewModel CreateViewModel(
        bool onlineMode,
        Profile profile,
        HttpClient? httpClient = null)
        => CreateViewModel(CreateSettingsStore(), onlineMode, profile, httpClient);

    private static SettingsViewModel CreateViewModel(
        Mock<IDesktopSettingsStore> settings,
        bool onlineMode,
        Profile profile,
        HttpClient? httpClient = null)
    {
        settings.Object.OnlineMode = onlineMode;
        var profiles = new Mock<IProfileRepository>();
        profiles.Setup(repository => repository.GetSelectedProfile()).Returns(profile);

        return new SettingsViewModel(
            settings.Object,
            new Mock<IExternalUriLauncher>().Object,
            new StringLocalizer("en-US"),
            profileRepository: profiles.Object,
            httpClient: httpClient);
    }

    private static Mock<IDesktopSettingsStore> CreateSettingsStore()
    {
        var settings = new Mock<IDesktopSettingsStore>();
        settings.SetupGet(service => service.Language).Returns("en-US");
        settings.SetupGet(service => service.GpuPreference).Returns("auto");
        settings.SetupProperty(service => service.OnlineMode, true);
        settings.SetupProperty(service => service.AuthDomain, "sessions.sanasol.ws");
        settings.SetupProperty(service => service.AuthServers, Array.Empty<string>());
        settings.SetupProperty(service => service.JavaArguments, string.Empty);
        settings.SetupProperty(service => service.CustomJavaPath, string.Empty);
        settings.SetupProperty(service => service.GameEnvironmentVariables, string.Empty);
        return settings;
    }

    private sealed class AuthServerHandler(
        HttpStatusCode statusCode,
        string? body = null,
        string mediaType = "application/json") : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode);
            if (body is not null)
            {
                response.Content = new StringContent(body, Encoding.UTF8, mediaType);
            }
            else if (statusCode == HttpStatusCode.OK)
            {
                response.Content = new StringContent(
                    "{\"token\":\"probe-token\"}",
                    Encoding.UTF8,
                    "application/json");
            }

            return Task.FromResult(response);
        }
    }
}
