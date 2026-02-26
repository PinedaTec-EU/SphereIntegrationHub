namespace SphereIntegrationHub.MCP.Services.Catalog;

internal static class SwaggerUriResolver
{
    public static Uri Resolve(ApiCatalogVersion version, ApiDefinition definition, string environment)
    {
        if (Uri.TryCreate(definition.SwaggerUrl, UriKind.Absolute, out var absolute))
            return absolute;

        if (!TryResolveBaseUrl(version, definition, environment, out var baseUrl))
        {
            throw new InvalidOperationException(
                $"Cannot resolve relative swaggerUrl '{definition.SwaggerUrl}' because baseUrl is missing for version '{version.Version}' and definition '{definition.Name}'.");
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException(
                $"Invalid baseUrl '{baseUrl}' for version '{version.Version}'.");
        }

        return new Uri(baseUri, definition.SwaggerUrl.TrimStart('/'));
    }

    private static bool TryResolveBaseUrl(
        ApiCatalogVersion version, ApiDefinition definition, string environment, out string? baseUrl)
    {
        if (definition.BaseUrl is { Count: > 0 } &&
            TryResolveFromMap(definition.BaseUrl, environment, out baseUrl))
        {
            return true;
        }

        return TryResolveFromMap(version.BaseUrl, environment, out baseUrl);
    }

    private static bool TryResolveFromMap(
        IReadOnlyDictionary<string, string> map, string environment, out string? baseUrl)
    {
        if (map.TryGetValue(environment, out baseUrl) && !string.IsNullOrWhiteSpace(baseUrl))
            return true;

        baseUrl = map.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        return !string.IsNullOrWhiteSpace(baseUrl);
    }
}
