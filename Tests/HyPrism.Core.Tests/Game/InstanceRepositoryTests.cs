// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core.Infrastructure;
using HyPrism.Core.Game.Instances;
using System.Text.Json;

namespace HyPrism.Core.Tests.Game;

public class InstanceRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly JsonConfigStore _config;
    private readonly InstanceRepository _svc;

    public InstanceRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "HyPrismInstanceRepoTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _config = new JsonConfigStore(_tempDir);
        _svc = new InstanceRepository(_tempDir, _config);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void CreateInstanceMeta_RaisesInstancesChanged()
    {
        var raised = 0;
        _svc.InstancesChanged += () => raised++;

        var meta = _svc.CreateInstanceMeta("release", 42);

        Assert.Equal(1, raised);
        Assert.Single(_svc.GetCachedInstances());
        Assert.Contains(
            Directory.EnumerateFiles(_svc.GetInstanceRoot()),
            path => string.Equals(Path.GetFileName(path), "Instances.json", StringComparison.Ordinal));
        var instancePath = _svc.GetInstancePathById(meta.Id)!;
        var metaPath = Assert.Single(
            Directory.EnumerateFiles(instancePath),
            path => string.Equals(Path.GetFileName(path), "Meta.json", StringComparison.Ordinal));
        using var document = JsonDocument.Parse(File.ReadAllText(metaPath));
        Assert.All(
            document.RootElement.EnumerateObject(),
            property => Assert.True(char.IsUpper(property.Name[0]), property.Name));
    }

    [Fact]
    public void DeleteGameById_RemovesInstanceFromCacheAndRaisesInstancesChanged()
    {
        var meta = _svc.CreateInstanceMeta("release", 42);
        var raised = 0;
        _svc.InstancesChanged += () => raised++;

        Assert.True(_svc.DeleteGameById(meta.Id));

        Assert.Equal(1, raised);
        Assert.DoesNotContain(_svc.GetCachedInstances(), instance => instance.Id == meta.Id);
    }

    [Fact]
    public void SetInstanceOrder_PersistsOrderAndRaisesInstancesChanged()
    {
        var first = _svc.CreateInstanceMeta("release", 42);
        var second = _svc.CreateInstanceMeta("pre-release", 41);
        var raised = 0;
        _svc.InstancesChanged += () => raised++;

        _svc.SetInstanceOrder([second.Id, first.Id]);

        Assert.Equal(1, raised);
        Assert.Equal(
            [second.Id, first.Id],
            _svc.GetCachedInstances().Select(instance => instance.Id));
    }

    [Fact]
    public void SetSelectedInstance_RaisesInstancesChanged()
    {
        var meta = _svc.CreateInstanceMeta("release", 42);
        var raised = 0;
        _svc.InstancesChanged += () => raised++;

        _svc.SetSelectedInstance(meta.Id);

        Assert.Equal(1, raised);
        Assert.Equal(meta.Id, _svc.GetSelectedInstance()?.Id);
    }

    [Fact]
    public void ChangeInstanceVersion_RaisesInstancesChanged()
    {
        var meta = _svc.CreateInstanceMeta("release", 42);
        var raised = 0;
        _svc.InstancesChanged += () => raised++;

        Assert.True(_svc.ChangeInstanceVersion(meta.Id, "release", 43));

        Assert.Equal(1, raised);
        Assert.Equal(43, _svc.GetCachedInstances().Single().Version);
    }

    [Fact]
    public void SyncInstancesWithConfig_RaisesInstancesChanged()
    {
        var raised = 0;
        _svc.InstancesChanged += () => raised++;

        _svc.SyncInstancesWithConfig();

        Assert.Equal(1, raised);
    }

    [Fact]
    public void SaveInstanceMeta_DoesNotRaiseInstancesChanged()
    {
        var meta = _svc.CreateInstanceMeta("release", 42);
        var path = _svc.GetInstancePathById(meta.Id)!;
        var raised = 0;
        _svc.InstancesChanged += () => raised++;

        var instanceMeta = _svc.GetInstanceMeta(path)!;
        instanceMeta.PlayTimeSeconds += 60;
        _svc.SaveInstanceMeta(path, instanceMeta);

        Assert.Equal(0, raised);
    }
}
