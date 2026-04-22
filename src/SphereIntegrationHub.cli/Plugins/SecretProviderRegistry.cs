using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.Plugins;

public sealed class SecretProviderRegistry
{
    private readonly Dictionary<string, ISecretProviderPlugin> _pluginsById;

    public SecretProviderRegistry(IEnumerable<ISecretProviderPlugin> plugins)
    {
        _pluginsById = new Dictionary<string, ISecretProviderPlugin>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in plugins)
        {
            if (!_pluginsById.TryAdd(plugin.Descriptor.Id, plugin))
            {
                throw new InvalidOperationException($"Secret provider plugin '{plugin.Descriptor.Id}' is already registered.");
            }
        }
    }

    public bool TryGet(string pluginId, out ISecretProviderPlugin plugin)
        => _pluginsById.TryGetValue(pluginId, out plugin!);
}

public sealed class SecretProviderRegistryBuilder
{
    public SecretProviderRegistry Build()
        => new([new VaultwardenPlugin.VaultwardenSecretProviderPlugin()]);
}
