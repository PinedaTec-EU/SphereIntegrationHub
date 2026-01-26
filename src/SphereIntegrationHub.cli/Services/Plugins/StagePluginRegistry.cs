using System;
using System.Collections.Generic;
using System.Linq;

namespace SphereIntegrationHub.Services.Plugins;

public sealed class StagePluginRegistry
{
    private readonly IReadOnlyCollection<IStagePlugin> _plugins;
    private readonly IReadOnlyDictionary<string, IStagePlugin> _pluginsByKind;
    private readonly IReadOnlyDictionary<string, IStagePlugin> _pluginsById;

    public StagePluginRegistry(IReadOnlyDictionary<string, IStagePlugin> pluginsByKind, IReadOnlyDictionary<string, IStagePlugin> pluginsById)
    {
        _pluginsByKind = pluginsByKind ?? throw new ArgumentNullException(nameof(pluginsByKind));
        _pluginsById = pluginsById ?? throw new ArgumentNullException(nameof(pluginsById));
        _plugins = _pluginsById.Values.ToArray();
    }

    public IReadOnlyCollection<IStagePlugin> Plugins => _plugins;

    public bool TryGetByKind(string kind, out IStagePlugin plugin)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            plugin = null!;
            return false;
        }

        return _pluginsByKind.TryGetValue(kind, out plugin!);
    }

    public bool TryGetById(string id, out IStagePlugin plugin)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            plugin = null!;
            return false;
        }

        return _pluginsById.TryGetValue(id, out plugin!);
    }
}
