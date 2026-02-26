using SphereIntegrationHub.MCP.Services.Catalog;

namespace SphereIntegrationHub.MCP.Services;

internal static class VersionResolver
{
    public static async Task<string> ResolveAsync(
        string? requested, ApiCatalogReader reader, List<string> warnings)
    {
        if (!string.IsNullOrWhiteSpace(requested))
            return requested;

        var versions = await reader.GetVersionsAsync();
        var fallback = versions.FirstOrDefault()
            ?? throw new InvalidOperationException(
                "version was not provided and no catalog versions are available");

        warnings.Add($"version was not provided; using first catalog version '{fallback}'.");
        return fallback;
    }
}
