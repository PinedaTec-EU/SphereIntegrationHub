namespace SphereIntegrationHub.Definitions;

public sealed class ApiCatalogVersion
{
    public required string Version { get; set; }
    public required Dictionary<string, string> BaseUrl { get; set; }
    public required List<ApiDefinition> Definitions { get; set; }
}
