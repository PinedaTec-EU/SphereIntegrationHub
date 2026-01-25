namespace SphereIntegrationHub.Definitions;

public sealed record ApiDefinition(
    string Name,
    string SwaggerUrl,
    IReadOnlyDictionary<string, string>? BaseUrl,
    string? BasePath);
