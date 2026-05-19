namespace SphereIntegrationHub.Definitions;

public sealed class ApiCatalogVersion
{
    public required string Version { get; set; }
    public required List<ApiDefinition> Definitions { get; set; }
    public List<LatencyProfileDefinition>? LatencyProfiles { get; set; }
    public List<ApiConnectionDefinition>? Connections { get; set; }
    public List<PluginCatalogDefinition>? Plugins { get; set; }
}

public sealed class ApiConnectionDefinition
{
    public required string Name { get; set; }
    public string? Type { get; set; }
    public string? Provider { get; set; }
    public Dictionary<string, string>? BaseUrl { get; set; }
    public int? Port { get; set; }
    public string? BasePath { get; set; }
    public string? ApiKey { get; set; }
    public string? ApiKeySecret { get; set; }
    public Dictionary<string, object?>? Config { get; set; }
}

public sealed class PluginCatalogDefinition
{
    public string Id { get; set; } = string.Empty;
    public string? Assembly { get; set; }
    public string ContractVersion { get; set; } = "1.0";
    public string? RuntimeVersion { get; set; }
}
