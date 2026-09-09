// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core.Game.Instances;
using HyPrism.Core.Migrations;
using HyPrism.Core.Game.Versions;
using HyPrism.Core.Models;

namespace HyPrism.Core.Tests.Game.Instances;

public sealed class InstanceVersionNameMigratorTests
{
    [Fact]
    public async Task FillsMissingVersionNamesAndResynchronizesInstances()
    {
        var instances = new Mock<IInstanceRepository>();
        var versions = new Mock<IGameVersionCatalog>();
        var instance = new InstanceInfo
        {
            Id = "legacy-instance",
            Name = "Legacy instance",
            Branch = "release",
            Version = 102
        };
        var meta = new InstanceMeta
        {
            Id = instance.Id,
            Name = instance.Name,
            Branch = instance.Branch,
            Version = instance.Version
        };

        instances.Setup(service => service.GetCachedInstances()).Returns([instance]);
        instances.Setup(service => service.GetInstancePathById(instance.Id)).Returns("/tmp/legacy-instance");
        instances.Setup(service => service.GetInstanceMeta("/tmp/legacy-instance")).Returns(meta);
        versions
            .Setup(service => service.GetVersionListWithSourcesAsync(
                "release",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VersionListResponse
            {
                Versions =
                [
                    new VersionInfo
                    {
                        Version = 102,
                        VersionName = "0.6.4"
                    }
                ]
            });

        var migrator = new InstanceVersionNameMigrator(instances.Object, versions.Object);

        await migrator.MigrateAsync();

        Assert.Equal("0.6.4", meta.VersionName);
        instances.Verify(
            service => service.SaveInstanceMeta(
                "/tmp/legacy-instance",
                It.Is<InstanceMeta>(saved => saved.VersionName == "0.6.4")),
            Times.Once);
        instances.Verify(service => service.SyncInstancesWithConfig(), Times.Once);
    }
}
