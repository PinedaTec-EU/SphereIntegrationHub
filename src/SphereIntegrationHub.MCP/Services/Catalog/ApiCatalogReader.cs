using System.Text.Json;
using SphereIntegrationHub.MCP.Services.Integration;

namespace SphereIntegrationHub.MCP.Services.Catalog;

/// <summary>
/// Reads and parses the API catalog configuration
/// </summary>
public sealed class ApiCatalogReader
{
    private readonly SihServicesAdapter _adapter;
    private List<ApiCatalogVersion>? _cachedCatalog;

    public ApiCatalogReader(SihServicesAdapter adapter)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    }

    public async Task<List<ApiCatalogVersion>> GetCatalogAsync()
    {
        if (_cachedCatalog != null)
        {
            return _cachedCatalog;
        }

        var json = await File.ReadAllTextAsync(_adapter.ApiCatalogPath);
        _cachedCatalog = JsonSerializer.Deserialize<List<ApiCatalogVersion>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];

        return _cachedCatalog;
    }

    public async Task<List<string>> GetVersionsAsync()
    {
        var catalog = await GetCatalogAsync();
        return catalog.Select(v => v.Version).ToList();
    }

    public async Task<ApiCatalogVersion?> GetVersionAsync(string version)
    {
        var catalog = await GetCatalogAsync();
        return catalog.FirstOrDefault(v => v.Version.Equals(version, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<List<ApiDefinition>> GetApiDefinitionsAsync(string version)
    {
        var versionInfo = await GetVersionAsync(version);
        return versionInfo?.Definitions ?? [];
    }

    public async Task<ApiDefinition?> GetApiDefinitionAsync(string version, string apiName)
    {
        var definitions = await GetApiDefinitionsAsync(version);
        return definitions.FirstOrDefault(d => d.Name.Equals(apiName, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class ApiCatalogVersion
{
    public required string Version { get; set; }
    public required Dictionary<string, string> BaseUrl { get; set; }
    public required List<ApiDefinition> Definitions { get; set; }
}

public sealed class ApiDefinition
{
    public required string Name { get; set; }
    public required string BasePath { get; set; }
    public required string SwaggerUrl { get; set; }
}
