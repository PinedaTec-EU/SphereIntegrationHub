using System;
using System.Collections.Generic;
using System.Linq;

namespace SphereIntegrationHub.Services.Plugins;

internal static class BuiltInStagePlugins
{
    public static IReadOnlyDictionary<string, IStagePlugin> CreateCatalog()
    {
        var plugins = new IStagePlugin[]
        {
            new WorkflowStagePlugin(),
            new HttpStagePlugin()
        };

        return plugins.ToDictionary(plugin => plugin.Id, StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyDictionary<string, IStageValidator> CreateValidators()
    {
        var validators = new IStageValidator[]
        {
            new WorkflowStagePlugin(),
            new HttpStagePlugin()
        };

        return validators.ToDictionary(validator => validator.Id, StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyCollection<string> RequiredPluginIds { get; } = new[]
    {
        "workflow"
    };
}
