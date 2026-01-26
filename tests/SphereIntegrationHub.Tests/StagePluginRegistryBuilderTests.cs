using System;
using System.Collections.Generic;
using System.IO;

using SphereIntegrationHub.cli;
using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services.Plugins;

namespace SphereIntegrationHub.Tests;

public sealed class StagePluginRegistryBuilderTests
{
    [Fact]
    public void TryBuild_FailsWhenNoPluginsConfigured()
    {
        var config = new WorkflowConfig
        {
            ConfigPath = "/tmp/workflows.config"
        };
        var builder = new StagePluginRegistryBuilder(
            BuiltInStagePlugins.CreateCatalog(),
            BuiltInStagePlugins.CreateValidators(),
            BuiltInStagePlugins.RequiredPluginIds);

        var ok = builder.TryBuild(config, out _, out _, out var errors);

        Assert.False(ok);
        Assert.Contains(errors, error => error.Contains("No plugins were configured", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("built-in workflow plugin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TryBuild_LoadsBuiltInHttpPlugin()
    {
        var config = new WorkflowConfig
        {
            Plugins = new List<string> { "http" },
            ConfigPath = "/tmp/workflows.config"
        };
        var builder = new StagePluginRegistryBuilder(
            BuiltInStagePlugins.CreateCatalog(),
            BuiltInStagePlugins.CreateValidators(),
            BuiltInStagePlugins.RequiredPluginIds);

        var ok = builder.TryBuild(config, out var registry, out var validators, out var errors);

        Assert.True(ok);
        Assert.Empty(errors);
        Assert.True(registry.TryGetById("workflow", out _));
        Assert.True(registry.TryGetById("http", out var httpPlugin));
        Assert.True(validators.TryGetById("http", out _));
        Assert.Contains(WorkflowStageKinds.Endpoint, httpPlugin.StageKinds);
    }

    [Fact]
    public void TryBuild_FailsWhenPluginDllMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"plugins-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var config = new WorkflowConfig
        {
            Plugins = new List<string> { "missing" },
            ConfigPath = Path.Combine(root, CliConstants.WorkflowConfigFileName)
        };
        var builder = new StagePluginRegistryBuilder(
            BuiltInStagePlugins.CreateCatalog(),
            BuiltInStagePlugins.CreateValidators(),
            BuiltInStagePlugins.RequiredPluginIds);

        var ok = builder.TryBuild(config, out _, out _, out var errors);

        Assert.False(ok);
        Assert.Contains(errors, error => error.Contains("was not found", StringComparison.OrdinalIgnoreCase));
    }
}
