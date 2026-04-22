namespace SphereIntegrationHub.Definitions;

public interface ISecretProviderPlugin
{
    SecretProviderDescriptor Descriptor { get; }

    Task<SecretProviderResult> ResolveAsync(
        SecretProviderDefinition definition,
        SecretProviderExecutionContext context,
        CancellationToken cancellationToken);
}

public abstract class SecretProviderPluginBase : ISecretProviderPlugin
{
    protected SecretProviderPluginBase(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Descriptor = new SecretProviderDescriptor(
            id,
            StagePluginContract.Version,
            GetType().Assembly.GetName().Version?.ToString() ?? StagePluginContract.Version);
    }

    public SecretProviderDescriptor Descriptor { get; }

    public abstract Task<SecretProviderResult> ResolveAsync(
        SecretProviderDefinition definition,
        SecretProviderExecutionContext context,
        CancellationToken cancellationToken);
}

public sealed record SecretProviderDescriptor(
    string Id,
    string ContractVersion,
    string RuntimeVersion);

public sealed class SecretProviderDefinition
{
    public string Plugin { get; set; } = string.Empty;
    public Dictionary<string, object?>? Config { get; set; }

    public string? GetConfigString(string key)
        => Config is not null && Config.TryGetValue(key, out var value)
            ? value?.ToString()
            : null;

    public Dictionary<string, string>? GetConfigStringDictionary(string key)
    {
        if (Config is null || !Config.TryGetValue(key, out var value))
        {
            return null;
        }

        return value switch
        {
            Dictionary<string, string> textDictionary => new(textDictionary, StringComparer.OrdinalIgnoreCase),
            Dictionary<string, object?> objectDictionary => objectDictionary.ToDictionary(
                pair => pair.Key,
                pair => pair.Value?.ToString() ?? string.Empty,
                StringComparer.OrdinalIgnoreCase),
            IDictionary<object, object> looseDictionary => looseDictionary.ToDictionary(
                pair => pair.Key.ToString() ?? string.Empty,
                pair => pair.Value?.ToString() ?? string.Empty,
                StringComparer.OrdinalIgnoreCase),
            _ => null
        };
    }
}

public sealed record SecretProviderExecutionContext(
    HttpClient HttpClient,
    IReadOnlyDictionary<string, string> ProcessEnvironment);

public sealed record SecretProviderResult(
    IReadOnlyDictionary<string, string> Secrets,
    IReadOnlyCollection<string> SecretValues);
