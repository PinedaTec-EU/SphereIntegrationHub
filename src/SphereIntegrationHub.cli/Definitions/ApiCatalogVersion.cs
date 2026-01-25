namespace SphereIntegrationHub.Definitions;

public sealed record ApiCatalogVersion(
    string Version,
    IReadOnlyDictionary<string, string> BaseUrl,
    IReadOnlyList<ApiDefinition> Definitions);
