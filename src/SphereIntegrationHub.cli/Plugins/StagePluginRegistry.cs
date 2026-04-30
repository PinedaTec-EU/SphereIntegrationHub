using System.Reflection;

using SphereIntegrationHub.cli;
using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.Plugins;

public sealed class StagePluginRegistry
{
    private readonly Dictionary<string, IStagePlugin> _pluginsById;
    private readonly Dictionary<string, IStagePlugin> _pluginsByKind;

    public StagePluginRegistry(IEnumerable<IStagePlugin> plugins)
    {
        ArgumentNullException.ThrowIfNull(plugins);

        _pluginsById = new Dictionary<string, IStagePlugin>(StringComparer.OrdinalIgnoreCase);
        _pluginsByKind = new Dictionary<string, IStagePlugin>(StringComparer.OrdinalIgnoreCase);

        foreach (var plugin in plugins)
        {
            Register(plugin);
        }
    }

    public IReadOnlyCollection<IStagePlugin> Plugins => _pluginsById.Values.ToArray();

    public bool TryGetByKind(string? kind, out IStagePlugin plugin)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            plugin = null!;
            return false;
        }

        return _pluginsByKind.TryGetValue(kind, out plugin!);
    }

    public bool TryGetById(string pluginId, out IStagePlugin plugin)
        => _pluginsById.TryGetValue(pluginId, out plugin!);

    private void Register(IStagePlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        if (!_pluginsById.TryAdd(plugin.Descriptor.Id, plugin))
        {
            throw new InvalidOperationException($"Plugin id '{plugin.Descriptor.Id}' is already registered.");
        }

        foreach (var kind in plugin.Descriptor.StageKinds)
        {
            if (!_pluginsByKind.TryAdd(kind, plugin))
            {
                throw new InvalidOperationException($"Stage kind '{kind}' is already registered.");
            }
        }
    }
}

public sealed class StagePluginRegistryBuilder
{
    private static readonly string[] DefaultPluginIds = [HttpStagePluginId];
    private const string HttpStagePluginId = "http";
    private const string OpenAIStagePluginId = "openai";

    public StagePluginRegistry Build(WorkflowConfig config, ApiCatalogVersion catalogVersion, string workflowPath)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(catalogVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowPath);

        var requestedPluginIds = ResolveRequestedPluginIds(config);
        var plugins = new List<IStagePlugin>();

        foreach (var pluginId in requestedPluginIds)
        {
            plugins.Add(LoadPlugin(pluginId, catalogVersion, workflowPath, config.Plugins is { Count: > 0 }));
        }

        return new StagePluginRegistry(plugins);
    }

    public StagePluginRegistry CreateBuiltInRegistry()
        => new([new HttpPlugin.HttpStagePlugin(), new OpenAIPlugin.OpenAIStagePlugin()]);

    private static IReadOnlyList<string> ResolveRequestedPluginIds(WorkflowConfig config)
    {
        if (config.Plugins is not { Count: > 0 })
        {
            return DefaultPluginIds;
        }

        return config.Plugins
            .Where(pluginId => !string.IsNullOrWhiteSpace(pluginId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IStagePlugin LoadPlugin(
        string pluginId,
        ApiCatalogVersion catalogVersion,
        string workflowPath,
        bool requireCatalogDeclaration)
    {
        if (string.Equals(pluginId, HttpStagePluginId, StringComparison.OrdinalIgnoreCase))
        {
            return new HttpPlugin.HttpStagePlugin();
        }

        if (string.Equals(pluginId, OpenAIStagePluginId, StringComparison.OrdinalIgnoreCase))
        {
            return new OpenAIPlugin.OpenAIStagePlugin();
        }

        var catalogPlugin = catalogVersion.Plugins?.FirstOrDefault(item =>
            string.Equals(item.Id, pluginId, StringComparison.OrdinalIgnoreCase));

        if (catalogPlugin is null)
        {
            var suffix = requireCatalogDeclaration
                ? $" Add it under plugins in catalog version '{catalogVersion.Version}'."
                : string.Empty;
            throw new InvalidOperationException($"Plugin '{pluginId}' is not declared in api.catalog.{suffix}");
        }

        var pluginDirectory = Path.Combine(Path.GetDirectoryName(workflowPath) ?? Directory.GetCurrentDirectory(), "plugins");
        var assemblyFileName = string.IsNullOrWhiteSpace(catalogPlugin.Assembly)
            ? $"{pluginId}.dll"
            : catalogPlugin.Assembly;
        var assemblyPath = Path.Combine(pluginDirectory, assemblyFileName);
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException($"Plugin '{pluginId}' assembly was not found.", assemblyPath);
        }

        var assembly = Assembly.LoadFrom(assemblyPath);
        var pluginType = assembly
            .GetTypes()
            .FirstOrDefault(type =>
                !type.IsAbstract &&
                typeof(IStagePlugin).IsAssignableFrom(type));

        if (pluginType is null)
        {
            throw new InvalidOperationException($"Assembly '{assemblyPath}' does not expose any IStagePlugin implementation.");
        }

        if (Activator.CreateInstance(pluginType) is not IStagePlugin plugin)
        {
            throw new InvalidOperationException($"Plugin '{pluginType.FullName}' could not be instantiated.");
        }

        EnsureCompatibleContract(plugin, catalogPlugin);
        return plugin;
    }

    private static void EnsureCompatibleContract(IStagePlugin plugin, PluginCatalogDefinition catalogPlugin)
    {
        if (!IsCompatibleVersion(plugin.Descriptor.ContractVersion, catalogPlugin.ContractVersion))
        {
            throw new InvalidOperationException(
                $"Plugin '{plugin.Descriptor.Id}' contract version '{plugin.Descriptor.ContractVersion}' is not compatible with catalog version '{catalogPlugin.ContractVersion}'.");
        }

        if (!string.IsNullOrWhiteSpace(catalogPlugin.RuntimeVersion) &&
            !IsCompatibleVersion(plugin.Descriptor.RuntimeVersion, catalogPlugin.RuntimeVersion))
        {
            throw new InvalidOperationException(
                $"Plugin '{plugin.Descriptor.Id}' runtime version '{plugin.Descriptor.RuntimeVersion}' is not compatible with catalog runtime '{catalogPlugin.RuntimeVersion}'.");
        }
    }

    private static bool IsCompatibleVersion(string actual, string expected)
    {
        if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var actualMajor = actual.Split('.', StringSplitOptions.RemoveEmptyEntries)[0];
        var expectedMajor = expected.Split('.', StringSplitOptions.RemoveEmptyEntries)[0];
        return string.Equals(actualMajor, expectedMajor, StringComparison.OrdinalIgnoreCase);
    }
}
