namespace SphereIntegrationHub.MCP.Services.Integration;

/// <summary>
/// Adapter/bridge to SphereIntegrationHub.cli services.
/// Provides a clean interface for MCP tools to access existing CLI functionality.
/// </summary>
public sealed class SihServicesAdapter
{
    private readonly string _projectRoot;

    public SihServicesAdapter(string projectRoot)
    {
        _projectRoot = projectRoot ?? throw new ArgumentNullException(nameof(projectRoot));

        if (!Directory.Exists(projectRoot))
        {
            throw new DirectoryNotFoundException($"Project root not found: {projectRoot}");
        }

        // Initialize paths
        ResourcesPath = Path.Combine(_projectRoot, "src", "resources");
        CachePath = Path.Combine(ResourcesPath, "cache");
        WorkflowsPath = Path.Combine(ResourcesPath, "workflows");
        ApiCatalogPath = Path.Combine(ResourcesPath, "api-catalog.json");

        // Verify critical paths exist
        if (!File.Exists(ApiCatalogPath))
        {
            throw new FileNotFoundException($"API catalog not found: {ApiCatalogPath}");
        }
    }

    public string ProjectRoot => _projectRoot;
    public string ResourcesPath { get; }
    public string CachePath { get; }
    public string WorkflowsPath { get; }
    public string ApiCatalogPath { get; }

    /// <summary>
    /// Gets the path to a cached Swagger file
    /// </summary>
    public string GetSwaggerCachePath(string version, string apiName)
    {
        return Path.Combine(CachePath, version, $"{apiName}.json");
    }

    /// <summary>
    /// Checks if a Swagger cache file exists
    /// </summary>
    public bool SwaggerCacheExists(string version, string apiName)
    {
        return File.Exists(GetSwaggerCachePath(version, apiName));
    }

    /// <summary>
    /// Gets all available versions from cache directory
    /// </summary>
    public List<string> GetCachedVersions()
    {
        if (!Directory.Exists(CachePath))
        {
            return [];
        }

        return Directory.GetDirectories(CachePath)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToList()!;
    }

    /// <summary>
    /// Gets all cached API definitions for a version
    /// </summary>
    public List<string> GetCachedApiDefinitions(string version)
    {
        var versionPath = Path.Combine(CachePath, version);
        if (!Directory.Exists(versionPath))
        {
            return [];
        }

        return Directory.GetFiles(versionPath, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToList()!;
    }

    /// <summary>
    /// Gets the path to workflows directory
    /// </summary>
    public string GetWorkflowsPath()
    {
        return WorkflowsPath;
    }
}
