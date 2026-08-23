// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using System.Text;
using System.Text.Json;
using HyPrism.Core.Accounts;
using HyPrism.Core.Game.Sources;
using HyPrism.Core.Infrastructure;
using HyPrism.Core.Models;

namespace HyPrism.Core.Tests.Game.Sources;

public sealed class HytaleVersionSourceTests
{
    [Fact]
    public async Task OfficialAvailabilityProbeUsesAuthenticatedPatchesEndpoint()
    {
        var appDir = Path.Combine(Path.GetTempPath(), $"hyprism-official-probe-{Guid.NewGuid():N}");
        var profile = new Profile
        {
            Id = Guid.NewGuid().ToString(),
            UUID = Guid.NewGuid().ToString(),
            Name = "Official",
            IsOfficial = true
        };
        var profilesRoot = LauncherUtilities.GetProfilesRoot(appDir);
        Directory.CreateDirectory(profilesRoot);
        File.WriteAllText(
            Path.Combine(profilesRoot, "profiles.json"),
            JsonSerializer.Serialize(new[] { profile }));
        var profileDirectory = LauncherUtilities.GetProfileFolderPath(appDir, profile);
        File.WriteAllText(
            Path.Combine(profileDirectory, "hytale_session.json"),
            JsonSerializer.Serialize(new HytaleAuthSession
            {
                AccessToken = "official-access-token",
                RefreshToken = "official-refresh-token",
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                Username = profile.Name,
                UUID = profile.UUID
            }));

        try
        {
            var config = new Mock<IConfigStore>();
            config.SetupGet(service => service.Configuration).Returns(new Config
            {
                SelectedProfileId = profile.Id
            });
            var profiles = new Mock<IProfileManager>();
            profiles.Setup(service => service.GetProfiles()).Returns([profile]);
            var handler = new OfficialProbeHandler();
            using var httpClient = new HttpClient(handler);
            var authenticator = new HytaleAuthenticator(httpClient, appDir, config.Object);
            var source = new HytaleVersionSource(
                appDir,
                httpClient,
                authenticator,
                config.Object,
                profiles.Object);

            var result = await source.ProbeAvailabilityAsync();
            var cached = await source.ProbeAvailabilityAsync();

            Assert.True(source.IsAvailable);
            Assert.True(result.IsAvailable);
            Assert.True(cached.IsAvailable);
            Assert.Equal(1, handler.PatchesRequestCount);
            Assert.True(handler.PatchesRequestWasAuthenticated);
            Assert.False(handler.UnauthenticatedRootWasRequested);
        }
        finally
        {
            Directory.Delete(appDir, recursive: true);
        }
    }

    private sealed class OfficialProbeHandler : HttpMessageHandler
    {
        public int PatchesRequestCount { get; private set; }
        public bool PatchesRequestWasAuthenticated { get; private set; }
        public bool UnauthenticatedRootWasRequested { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (string.Equals(uri.Host, "launcher.hytale.com", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse("{\"version\":\"1.0.0\"}"));
            }

            if (string.Equals(uri.Host, "account-data.hytale.com", StringComparison.OrdinalIgnoreCase) &&
                uri.AbsolutePath.StartsWith("/patches/", StringComparison.Ordinal))
            {
                PatchesRequestCount++;
                PatchesRequestWasAuthenticated =
                    string.Equals(request.Headers.Authorization?.Scheme, "Bearer", StringComparison.Ordinal) &&
                    string.Equals(
                        request.Headers.Authorization?.Parameter,
                        "official-access-token",
                        StringComparison.Ordinal);
                return Task.FromResult(JsonResponse(
                    "{\"steps\":[{\"from\":0,\"to\":1,\"pwr\":\"https://cdn.hytale.com/game.pwr\",\"pwrHead\":\"https://cdn.hytale.com/game.pwr\",\"sig\":\"https://cdn.hytale.com/game.sig\"}]}"));
            }

            if (string.Equals(uri.Host, "account-data.hytale.com", StringComparison.OrdinalIgnoreCase))
                UnauthenticatedRootWasRequested = true;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage JsonResponse(string json)
            => new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
    }
}
