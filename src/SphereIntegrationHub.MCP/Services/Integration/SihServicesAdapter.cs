namespace SphereIntegrationHub.MCP.Services.Integration;

/// <summary>
/// Adapter/bridge to SphereIntegrationHub.cli services.
/// Provides a clean interface for MCP tools to access existing CLI functionality.
/// </summary>
public sealed class SihServicesAdapter
{
    private readonly string _projectRoot;

    public SihServicesAdapter(string projectRoot)
        : this(new SihPathOptions { ProjectRoot = projectRoot })
    {
    }

    public SihServicesAdapter(SihPathOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _projectRoot = string.IsNullOrWhiteSpace(options.ProjectRoot)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(options.ProjectRoot);

        if (!Directory.Exists(_projectRoot))
        {
            throw new DirectoryNotFoundException($"Project root not found: {_projectRoot}");
        }

        // Initialize paths (overrideable via options)
        var defaultResourcesPath = Path.Combine(_projectRoot, "src", "resources");
        ResourcesPath = ResolvePath(options.ResourcesPath ?? defaultResourcesPath);
        CachePath = ResolvePath(options.CachePath ?? Path.Combine(ResourcesPath, "cache"));
        WorkflowsPath = ResolvePath(options.WorkflowsPath ?? Path.Combine(ResourcesPath, "workflows"));
        ApiCatalogPath = ResolvePath(options.ApiCatalogPath ?? Path.Combine(ResourcesPath, "api-catalog.json"));

        // Verify critical paths exist
        if (!File.Exists(ApiCatalogPath))
        {
            throw new FileNotFoundException(
                $"API catalog not found: {ApiCatalogPath}. " +
                "Set SIH_API_CATALOG_PATH (or SIH_RESOURCES_PATH/SIH_PROJECT_ROOT) to configure custom locations.");
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

    private string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(Path.Combine(_projectRoot, path));
    }
}
