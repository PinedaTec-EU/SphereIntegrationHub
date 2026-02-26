namespace SphereIntegrationHub.MCP.Services.Catalog;

internal static class SwaggerUriResolver
{
    public static Uri Resolve(ApiCatalogVersion version, ApiDefinition definition, string environment)
        => CatalogUrlResolver.ResolveSwaggerUri(version, definition, environment);
}
