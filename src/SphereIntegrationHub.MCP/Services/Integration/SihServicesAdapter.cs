namespace SphereIntegrationHub.MCP.Services.Integration;

/// <summary>
/// Adapter/bridge to SphereIntegrationHub.cli services.
/// Provides a clean interface for MCP tools to access existing CLI functionality.
/// </summary>
public sealed class SihServicesAdapter
{
    private const string SphereFolderName = ".sphere";
    private const string LegacyResourcesRelativePath = "src/resources";
    private const string ApiCatalogFileName = "api-catalog.json";
    private const string CacheFolderName = "cache";
    private const string WorkflowsFolderName = "workflows";

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
        var defaultResourcesPath = ResolveDefaultResourcesPath(options);
        ResourcesPath = ResolvePath(options.ResourcesPath ?? defaultResourcesPath);
        CachePath = ResolvePath(options.CachePath ?? Path.Combine(ResourcesPath, CacheFolderName));
        WorkflowsPath = ResolvePath(options.WorkflowsPath ?? Path.Combine(ResourcesPath, WorkflowsFolderName));
        ApiCatalogPath = ResolvePath(options.ApiCatalogPath ?? Path.Combine(ResourcesPath, ApiCatalogFileName));

        ApiCatalogExists = File.Exists(ApiCatalogPath);
    }

    public string ProjectRoot => _projectRoot;
    public string ResourcesPath { get; }
    public string CachePath { get; }
    public string WorkflowsPath { get; }
    public string ApiCatalogPath { get; }
    public bool ApiCatalogExists { get; }

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

    private string ResolveDefaultResourcesPath(SihPathOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ResourcesPath))
        {
            return options.ResourcesPath;
        }

        var spherePath = Path.Combine(_projectRoot, SphereFolderName);
        var legacyPath = Path.Combine(_projectRoot, LegacyResourcesRelativePath);
        var legacyCatalogPath = Path.Combine(legacyPath, ApiCatalogFileName);

        // Backward compatibility:
        // if a legacy resources structure exists and .sphere is not present yet, keep using legacy.
        if (!Directory.Exists(spherePath) &&
            (Directory.Exists(legacyPath) || File.Exists(legacyCatalogPath)))
        {
            return legacyPath;
        }

        return spherePath;
    }
}
