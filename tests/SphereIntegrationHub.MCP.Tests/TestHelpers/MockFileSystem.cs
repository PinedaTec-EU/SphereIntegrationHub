using System.Text;

namespace SphereIntegrationHub.MCP.Tests.TestHelpers;

/// <summary>
/// Mock file system for testing without real files
/// </summary>
public class MockFileSystem : IDisposable
{
    private readonly string _tempRoot;
    private readonly Dictionary<string, string> _fileContents;

    public MockFileSystem()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"MCP_Test_{Guid.NewGuid()}");
        _fileContents = new Dictionary<string, string>();

        // Create directory structure
        Directory.CreateDirectory(_tempRoot);
        Directory.CreateDirectory(Path.Combine(_tempRoot, "src", "resources"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "src", "resources", "cache"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "src", "resources", "workflows"));
    }

    public string RootPath => _tempRoot;
    public string ResourcesPath => Path.Combine(_tempRoot, "src", "resources");
    public string CachePath => Path.Combine(_tempRoot, "src", "resources", "cache");
    public string WorkflowsPath => Path.Combine(_tempRoot, "src", "resources", "workflows");
    public string ApiCatalogPath => Path.Combine(ResourcesPath, "api-catalog.json");

    /// <summary>
    /// Adds a file to the mock file system
    /// </summary>
    public void AddFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_tempRoot, relativePath);
        var directory = Path.GetDirectoryName(fullPath);

        if (directory != null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, content);
        _fileContents[relativePath] = content;
    }

    /// <summary>
    /// Adds the API catalog file
    /// </summary>
    public void AddApiCatalog(string content)
    {
        AddFile("src/resources/api-catalog.json", content);
    }

    /// <summary>
    /// Adds a swagger file to cache
    /// </summary>
    public void AddSwaggerFile(string version, string apiName, string content)
    {
        var relativePath = $"src/resources/cache/{version}/{apiName}.json";
        AddFile(relativePath, content);
    }

    /// <summary>
    /// Adds a workflow file
    /// </summary>
    public void AddWorkflow(string filename, string content)
    {
        var relativePath = $"src/resources/workflows/{filename}";
        AddFile(relativePath, content);
    }

    /// <summary>
    /// Creates cached versions with sample swagger files
    /// </summary>
    public void SetupCachedVersions(params string[] versions)
    {
        foreach (var version in versions)
        {
            var versionPath = Path.Combine(CachePath, version);
            Directory.CreateDirectory(versionPath);

            // Add some sample swagger files
            AddSwaggerFile(version, "AccountsAPI", TestDataBuilder.CreateSampleSwagger("AccountsAPI"));
            AddSwaggerFile(version, "UsersAPI", TestDataBuilder.CreateSampleSwagger("UsersAPI"));
        }
    }

    /// <summary>
    /// Gets the full path for a relative path
    /// </summary>
    public string GetFullPath(string relativePath)
    {
        return Path.Combine(_tempRoot, relativePath);
    }

    /// <summary>
    /// Checks if a file exists
    /// </summary>
    public bool FileExists(string relativePath)
    {
        var fullPath = Path.Combine(_tempRoot, relativePath);
        return File.Exists(fullPath);
    }

    /// <summary>
    /// Reads file content
    /// </summary>
    public string ReadFile(string relativePath)
    {
        var fullPath = Path.Combine(_tempRoot, relativePath);
        return File.Exists(fullPath) ? File.ReadAllText(fullPath) : string.Empty;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try
            {
                Directory.Delete(_tempRoot, true);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }
}

/// <summary>
/// In-memory cache for testing
/// </summary>
public class InMemoryCache
{
    private readonly Dictionary<string, object> _cache = new();

    public void Set<T>(string key, T value)
    {
        _cache[key] = value!;
    }

    public T? Get<T>(string key)
    {
        if (_cache.TryGetValue(key, out var value))
        {
            return (T)value;
        }
        return default;
    }

    public bool TryGet<T>(string key, out T? value)
    {
        if (_cache.TryGetValue(key, out var obj))
        {
            value = (T)obj;
            return true;
        }
        value = default;
        return false;
    }

    public void Clear()
    {
        _cache.Clear();
    }

    public bool Contains(string key)
    {
        return _cache.ContainsKey(key);
    }
}
