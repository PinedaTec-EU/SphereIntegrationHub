namespace SphereIntegrationHub.Definitions;

public sealed class ApiCatalogVersion
{
    public required string Version { get; set; }
    public required List<ApiDefinition> Definitions { get; set; }
    public List<PluginCatalogDefinition>? Plugins { get; set; }
}

public sealed class PluginCatalogDefinition
{
    public string Id { get; set; } = string.Empty;
    public string? Assembly { get; set; }
    public string ContractVersion { get; set; } = "1.0";
    public string? RuntimeVersion { get; set; }
}
