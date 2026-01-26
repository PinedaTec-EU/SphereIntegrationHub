using System;
using System.Collections.Generic;

using SphereIntegrationHub.cli;
using SphereIntegrationHub.Services.Plugins;

namespace SphereIntegrationHub.Tests;

internal static class TestStagePlugins
{
    public static StagePluginRegistry CreateRegistry()
        => CreateRegistries().Plugins;

    public static StageValidatorRegistry CreateValidatorRegistry()
        => CreateRegistries().Validators;

    public static (StagePluginRegistry Plugins, StageValidatorRegistry Validators) CreateRegistries()
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
        if (!builder.TryBuild(config, out var plugins, out var validators, out var errors))
        {
            throw new InvalidOperationException($"Failed to build stage plugins: {string.Join(", ", errors)}");
        }

        return (plugins, validators);
    }
}
