using System.Text.RegularExpressions;
using SphereIntegrationHub.MCP.Core;
using SphereIntegrationHub.MCP.Services.Catalog;
using SphereIntegrationHub.MCP.Services.Integration;
using System.Text.Json;

namespace SphereIntegrationHub.MCP.Tools;

internal static class CatalogSwaggerUrlNormalizer
{
    public static string NormalizeForCatalog(string swaggerUrl)
    {
        if (string.IsNullOrWhiteSpace(swaggerUrl) || !TryGetPath(swaggerUrl, out var path))
        {
            return swaggerUrl;
        }

        if (!CanNormalizeToSwaggerJson(path))
        {
            return swaggerUrl;
        }

        if (Uri.TryCreate(swaggerUrl, UriKind.Absolute, out var absolute))
        {
            var jsonPath = BuildPreferredJsonPath(absolute.AbsolutePath);
            if (string.IsNullOrWhiteSpace(jsonPath))
            {
                return swaggerUrl;
            }

            var builder = new UriBuilder(absolute)
            {
                Path = jsonPath,
                Query = string.Empty,
                Fragment = string.Empty
            };
            return builder.Uri.AbsoluteUri;
        }

        var localPart = swaggerUrl.Split('?', '#')[0];
        var preferred = BuildPreferredJsonPath(localPart);
        return string.IsNullOrWhiteSpace(preferred) ? swaggerUrl : preferred;
    }

    private static bool TryGetPath(string swaggerUrl, out string path)
    {
        if (Uri.TryCreate(swaggerUrl, UriKind.Absolute, out var absolute))
        {
            path = absolute.AbsolutePath;
            return true;
        }

        path = swaggerUrl.Split('?', '#')[0];
        return !string.IsNullOrWhiteSpace(path);
    }

    private static bool CanNormalizeToSwaggerJson(string path)
    {
        var normalized = path.TrimEnd('/');
        if (normalized.EndsWith("/swagger", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalized.EndsWith("/swagger/index.html", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalized.EndsWith("/swagger/ui/index.html", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string BuildPreferredJsonPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var prefix = path;
        if (prefix.EndsWith("/index.html", StringComparison.OrdinalIgnoreCase))
        {
            prefix = prefix[..^"/index.html".Length];
        }
        else if (prefix.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            var slashIndex = prefix.LastIndexOf('/');
            prefix = slashIndex > 0 ? prefix[..slashIndex] : prefix;
        }

        prefix = prefix.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return path;
        }

        if (prefix.EndsWith("/swagger", StringComparison.OrdinalIgnoreCase))
        {
            return $"{prefix}/v1/swagger.json";
        }

        return $"{prefix}/swagger.json";
    }
}

internal static class CatalogSwaggerTemplateBuilder
{
    public static bool TryBuildTemplate(
        string swaggerUrl,
        IReadOnlyDictionary<string, string> versionBaseUrls,
        string preferredEnvironment,
        out string templatedSwaggerUrl,
        out int? port)
    {
        templatedSwaggerUrl = swaggerUrl;
        port = null;

        if (!Uri.TryCreate(swaggerUrl, UriKind.Absolute, out var swaggerUri))
        {
            return false;
        }

        if (!TryResolveTemplateEnvironment(versionBaseUrls, preferredEnvironment, swaggerUri, out var environmentKey))
        {
            return false;
        }

        var pathAndQuery = swaggerUri.PathAndQuery;
        var shouldIncludePort = !swaggerUri.IsDefaultPort;
        port = shouldIncludePort ? swaggerUri.Port : null;
        templatedSwaggerUrl = shouldIncludePort
            ? $"{{{{baseUrl.{environmentKey}}}}}:{{{{port}}}}{pathAndQuery}"
            : $"{{{{baseUrl.{environmentKey}}}}}{pathAndQuery}";

        return true;
    }

    private static bool TryResolveTemplateEnvironment(
        IReadOnlyDictionary<string, string> versionBaseUrls,
        string preferredEnvironment,
        Uri swaggerUri,
        out string environmentKey)
    {
        if (TryMatchEnvironment(versionBaseUrls, preferredEnvironment, swaggerUri, out environmentKey))
        {
            return true;
        }

        foreach (var pair in versionBaseUrls)
        {
            if (TryMatchEnvironment(versionBaseUrls, pair.Key, swaggerUri, out environmentKey))
            {
                return true;
            }
        }

        environmentKey = string.Empty;
        return false;
    }

    private static bool TryMatchEnvironment(
        IReadOnlyDictionary<string, string> versionBaseUrls,
        string environment,
        Uri swaggerUri,
        out string environmentKey)
    {
        environmentKey = string.Empty;
        if (!versionBaseUrls.TryGetValue(environment, out var baseUrl) || string.IsNullOrWhiteSpace(baseUrl))
        {
            foreach (var pair in versionBaseUrls)
            {
                if (pair.Key.Equals(environment, StringComparison.OrdinalIgnoreCase))
                {
                    baseUrl = pair.Value;
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(baseUrl) ||
            !Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            return false;
        }

        var schemeMatches = string.Equals(swaggerUri.Scheme, baseUri.Scheme, StringComparison.OrdinalIgnoreCase);
        var hostMatches = string.Equals(swaggerUri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase);
        if (!schemeMatches || !hostMatches)
        {
            return false;
        }

        environmentKey = environment;
        return true;
    }
}


/// <summary>
/// Generates and optionally writes an API catalog file for the target project.
/// </summary>
[McpTool("generate_api_catalog_file", "Generates api-catalog.json content and can write it to disk", Category = "Generation", Level = "L1")]
public sealed class GenerateApiCatalogFileTool : IMcpTool
{
    private readonly SihServicesAdapter _adapter;

    public GenerateApiCatalogFileTool(SihServicesAdapter adapter)
    {
        _adapter = adapter;
    }

    public string Name => "generate_api_catalog_file";
    public string Description => "Creates API catalog JSON for new projects. Use this when api-catalog.json does not exist yet.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            versions = new
            {
                type = "array",
                description = "Catalog versions with baseUrl and definitions",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        version = new { type = "string" },
                        baseUrl = new
                        {
                            type = "object",
                            additionalProperties = new { type = "string" }
                        },
                        definitions = new
                        {
                            type = "array",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    name = new { type = "string" },
                                    port = new { type = "integer" },
                                    basePath = new { type = "string" },
                                    swaggerUrl = new { type = "string" },
                                    baseUrl = new
                                    {
                                        type = "object",
                                        additionalProperties = new { type = "string" },
                                        description = "Optional per-definition baseUrl map. Overrides version baseUrl for runtime execution."
                                    }
                                },
                                required = new[] { "name", "basePath", "swaggerUrl" }
                            }
                        }
                    },
                    required = new[] { "version", "definitions" }
                }
            },
            outputPath = new { type = "string", description = "Optional output path (default: configured api-catalog path)" },
            writeToDisk = new { type = "boolean", description = "Write file to disk (default: true)" }
        },
        required = new[] { "versions" }
    };

    public Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        if (arguments?.TryGetValue("versions", out var versionsObj) != true ||
            versionsObj is not JsonElement versionsEl ||
            versionsEl.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("versions is required and must be an array");
        }

        var versions = new List<ApiCatalogVersion>();
        foreach (var versionItem in versionsEl.EnumerateArray())
        {
            var version = versionItem.TryGetProperty("version", out var versionEl)
                ? versionEl.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(version))
            {
                throw new ArgumentException("Each versions item must include 'version'");
            }

            var baseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (versionItem.TryGetProperty("baseUrl", out var baseUrlEl) &&
                baseUrlEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in baseUrlEl.EnumerateObject())
                {
                    baseUrl[prop.Name] = prop.Value.GetString() ?? string.Empty;
                }
            }

            if (baseUrl.Count == 0)
            {
                baseUrl["local"] = "http://localhost";
                baseUrl["pre"] = "https://pre.example.com";
                baseUrl["prod"] = "https://api.example.com";
            }

            if (!versionItem.TryGetProperty("definitions", out var defsEl) || defsEl.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException("Each versions item must include 'definitions' array");
            }

            var definitions = new List<ApiDefinition>();
            foreach (var definitionItem in defsEl.EnumerateArray())
            {
                var name = definitionItem.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                var port = definitionItem.TryGetProperty("port", out var portEl) && portEl.TryGetInt32(out var parsedPort)
                    ? parsedPort
                    : (int?)null;
                var basePath = definitionItem.TryGetProperty("basePath", out var basePathEl) ? basePathEl.GetString() : null;
                var swaggerUrl = definitionItem.TryGetProperty("swaggerUrl", out var swaggerEl) ? swaggerEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(name) ||
                    string.IsNullOrWhiteSpace(basePath) ||
                    string.IsNullOrWhiteSpace(swaggerUrl))
                {
                    throw new ArgumentException("Definition requires name, basePath, swaggerUrl");
                }

                var normalizedSwaggerUrl = CatalogSwaggerUrlNormalizer.NormalizeForCatalog(swaggerUrl!);
                var definitionBaseUrl = ParseDefinitionBaseUrl(definitionItem);
                if (definitionBaseUrl.Count == 0 &&
                    CatalogSwaggerTemplateBuilder.TryBuildTemplate(
                        normalizedSwaggerUrl,
                        baseUrl,
                        "local",
                        out var templatedSwaggerUrl,
                        out var inferredPort))
                {
                    normalizedSwaggerUrl = templatedSwaggerUrl;
                    port ??= inferredPort;
                }

                if (definitionBaseUrl.Count == 0 && !normalizedSwaggerUrl.Contains("{{baseUrl.", StringComparison.OrdinalIgnoreCase))
                {
                    definitionBaseUrl = InferDefinitionBaseUrl(normalizedSwaggerUrl, "local");
                }

                definitions.Add(new ApiDefinition
                {
                    Name = name!,
                    Port = port,
                    BasePath = basePath!,
                    SwaggerUrl = normalizedSwaggerUrl,
                    BaseUrl = definitionBaseUrl.Count > 0 ? definitionBaseUrl : null
                });
            }

            versions.Add(new ApiCatalogVersion
            {
                Version = version!,
                BaseUrl = baseUrl,
                Definitions = definitions
            });
        }

        var json = JsonSerializer.Serialize(versions, CreateCatalogJsonOptions());
        var writeToDisk = ToolArgumentParser.TryReadBool(arguments, "writeToDisk", true);
        var outputPath = arguments?.GetValueOrDefault("outputPath")?.ToString();
        outputPath = ResolveOutputPath(outputPath);

        if (writeToDisk)
        {
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(outputPath, json);
        }

        return Task.FromResult<object>(new
        {
            outputPath,
            writeToDisk,
            catalogJson = json,
            versionsCount = versions.Count
        });
    }

    private string ResolveOutputPath(string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return _adapter.ApiCatalogPath;
        }

        if (Path.IsPathRooted(outputPath))
        {
            return Path.GetFullPath(outputPath);
        }

        return Path.GetFullPath(Path.Combine(_adapter.ProjectRoot, outputPath));
    }

    private static Dictionary<string, string> ParseDefinitionBaseUrl(JsonElement definitionItem)
    {
        var baseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (definitionItem.TryGetProperty("baseUrl", out var baseUrlEl) &&
            baseUrlEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in baseUrlEl.EnumerateObject())
            {
                var value = property.Value.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    baseUrl[property.Name] = value;
                }
            }
        }

        return baseUrl;
    }

    private static Dictionary<string, string> InferDefinitionBaseUrl(string swaggerUrl, string environment)
    {
        if (string.IsNullOrWhiteSpace(swaggerUrl) ||
            !Uri.TryCreate(swaggerUrl, UriKind.Absolute, out var swaggerUri))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var authority = swaggerUri.IsDefaultPort
            ? $"{swaggerUri.Scheme}://{swaggerUri.Host}"
            : $"{swaggerUri.Scheme}://{swaggerUri.Host}:{swaggerUri.Port}";

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [environment] = authority
        };
    }

    private static JsonSerializerOptions CreateCatalogJsonOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }
}

/// <summary>
/// Creates or updates a single API definition in api-catalog.json and optionally downloads swagger cache.
/// </summary>
[McpTool("upsert_api_catalog_and_cache", "Creates/updates catalog definition from swagger URL and downloads cache file", Category = "Generation", Level = "L1")]
public sealed class UpsertApiCatalogAndCacheTool : IMcpTool
{
    private const string DefaultEnvironment = "pre";
    private readonly SihServicesAdapter _adapter;

    public UpsertApiCatalogAndCacheTool(SihServicesAdapter adapter)
    {
        _adapter = adapter;
    }

    public string Name => "upsert_api_catalog_and_cache";
    public string Description => "Upserts one definition into api-catalog.json (create if missing) and downloads swagger cache for it.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            version = new { type = "string", description = "Catalog version (e.g. 3.11)" },
            apiName = new { type = "string", description = "Definition name" },
            swaggerUrl = new { type = "string", description = "Swagger URL (absolute or relative)" },
            port = new { type = "integer", description = "Optional API port to apply on resolved baseUrl for runtime and swagger resolution" },
            basePath = new { type = "string", description = "API base path (e.g. /api/accounts)" },
            environment = new { type = "string", description = "Environment key to resolve relative swaggerUrl (default: pre)" },
            baseUrl = new
            {
                type = "object",
                description = "Version baseUrl map. Used when creating new version or merging provided keys."
            },
            downloadCache = new { type = "boolean", description = "Download cache after upsert (default: true)" },
            overwriteDefinition = new { type = "boolean", description = "Overwrite existing definition values (default: true)" }
        },
        required = new[] { "version", "apiName", "swaggerUrl" }
    };

    public async Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var version = arguments?.GetValueOrDefault("version")?.ToString()
            ?? throw new ArgumentException("version is required");
        var apiName = arguments?.GetValueOrDefault("apiName")?.ToString()
            ?? throw new ArgumentException("apiName is required");
        var swaggerUrl = arguments?.GetValueOrDefault("swaggerUrl")?.ToString()
            ?? throw new ArgumentException("swaggerUrl is required");
        swaggerUrl = CatalogSwaggerUrlNormalizer.NormalizeForCatalog(swaggerUrl);
        var port = arguments?.TryGetValue("port", out var portObj) == true &&
                   int.TryParse(portObj?.ToString(), out var parsedPort)
            ? parsedPort
            : (int?)null;
        var basePath = arguments?.GetValueOrDefault("basePath")?.ToString();
        var environment = arguments?.GetValueOrDefault("environment")?.ToString() ?? DefaultEnvironment;
        var downloadCache = ToolArgumentParser.TryReadBool(arguments, "downloadCache", true);
        var overwriteDefinition = ToolArgumentParser.TryReadBool(arguments, "overwriteDefinition", true);
        var requestedApiName = apiName;

        var baseUrlOverrides = ParseBaseUrlMap(arguments);

        var (catalog, catalogCreated) = await LoadCatalogAsync(_adapter.ApiCatalogPath);
        var versionEntry = catalog.FirstOrDefault(v =>
            v.Version.Equals(version, StringComparison.OrdinalIgnoreCase));

        var versionCreated = false;
        if (versionEntry == null)
        {
            versionEntry = new ApiCatalogVersion
            {
                Version = version,
                BaseUrl = baseUrlOverrides.Count > 0 ? baseUrlOverrides : CreateDefaultBaseUrlMap(),
                Definitions = []
            };
            catalog.Add(versionEntry);
            versionCreated = true;
        }
        else
        {
            foreach (var pair in baseUrlOverrides)
            {
                versionEntry.BaseUrl[pair.Key] = pair.Value;
            }
        }

        var basePathValue = string.IsNullOrWhiteSpace(basePath) ? "/" : basePath;
        var storedSwaggerUrl = swaggerUrl;
        var storedPort = port;
        if (CatalogSwaggerTemplateBuilder.TryBuildTemplate(
                swaggerUrl,
                versionEntry.BaseUrl,
                environment,
                out var templatedSwaggerUrl,
                out var inferredPort))
        {
            storedSwaggerUrl = templatedSwaggerUrl;
            storedPort ??= inferredPort;
        }

        string? prefetchedPayload = null;
        string? inferredApiName = null;

        if (downloadCache && IsGenericApiName(apiName))
        {
            try
            {
                var tempDefinition = new ApiDefinition
                {
                    Name = apiName,
                    Port = storedPort,
                    BasePath = basePathValue,
                    SwaggerUrl = storedSwaggerUrl,
                    BaseUrl = InferDefinitionBaseUrl(swaggerUrl, environment)
                };

                var swaggerUri = SwaggerUriResolver.Resolve(versionEntry,tempDefinition, environment);
                prefetchedPayload = await SwaggerDownloader.DownloadAsync(swaggerUri);
                var title = TryExtractOpenApiTitle(prefetchedPayload);
                inferredApiName = NormalizeApiName(title);
                if (!string.IsNullOrWhiteSpace(inferredApiName) &&
                    !inferredApiName.Equals(apiName, StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine(
                        $"[SphereIntegrationHub.MCP] Info: apiName '{apiName}' looks generic. Using inferred name '{inferredApiName}' from OpenAPI title.");
                    apiName = inferredApiName;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[SphereIntegrationHub.MCP] Warning: Could not infer a better apiName from OpenAPI title: {ex.Message}");
            }
        }

        var definition = versionEntry.Definitions.FirstOrDefault(d =>
            d.Name.Equals(apiName, StringComparison.OrdinalIgnoreCase));

        var definitionAction = "updated";
        if (definition == null)
        {
            definition = new ApiDefinition
            {
                Name = apiName,
                Port = storedPort,
                BasePath = basePathValue,
                SwaggerUrl = storedSwaggerUrl,
                BaseUrl = InferDefinitionBaseUrl(swaggerUrl, environment)
            };
            versionEntry.Definitions.Add(definition);
            definitionAction = "created";
        }
        else if (overwriteDefinition)
        {
            definition.BasePath = basePathValue;
            definition.SwaggerUrl = storedSwaggerUrl;
            definition.Port = storedPort;
            var inferredBaseUrl = InferDefinitionBaseUrl(swaggerUrl, environment);
            if (inferredBaseUrl.Count > 0)
            {
                definition.BaseUrl ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in inferredBaseUrl)
                {
                    definition.BaseUrl[pair.Key] = pair.Value;
                }
            }
        }
        else
        {
            definitionAction = "kept-existing";
        }

        await SaveCatalogAsync(_adapter.ApiCatalogPath, catalog);

        string? cachePath = null;
        if (downloadCache)
        {
            cachePath = await DownloadSwaggerToCacheAsync(versionEntry, definition, environment, true, prefetchedPayload);
        }

        return new
        {
            catalogPath = _adapter.ApiCatalogPath,
            catalogCreated,
            version,
            versionCreated,
            apiName,
            requestedApiName,
            inferredApiName,
            definitionAction,
            cacheDownloaded = downloadCache,
            cachePath
        };
    }

    private static Dictionary<string, string> ParseBaseUrlMap(Dictionary<string, object>? arguments)
    {
        if (arguments?.TryGetValue("baseUrl", out var baseUrlObj) != true)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        if (baseUrlObj is not JsonElement baseUrlEl || baseUrlEl.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("baseUrl must be an object");
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in baseUrlEl.EnumerateObject())
        {
            map[prop.Name] = prop.Value.GetString() ?? string.Empty;
        }

        return map;
    }

    private static async Task<(List<ApiCatalogVersion> Catalog, bool Created)> LoadCatalogAsync(string catalogPath)
    {
        if (!File.Exists(catalogPath))
        {
            return ([], true);
        }

        var json = await File.ReadAllTextAsync(catalogPath);
        var catalog = JsonSerializer.Deserialize<List<ApiCatalogVersion>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];

        return (catalog, false);
    }

    private static async Task SaveCatalogAsync(string catalogPath, List<ApiCatalogVersion> catalog)
    {
        var directory = Path.GetDirectoryName(catalogPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(catalog, CreateCatalogJsonOptions());
        await File.WriteAllTextAsync(catalogPath, json);
    }

    private static JsonSerializerOptions CreateCatalogJsonOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    private async Task<string> DownloadSwaggerToCacheAsync(
        ApiCatalogVersion versionEntry,
        ApiDefinition definition,
        string environment,
        bool refresh,
        string? prefetchedPayload = null)
    {
        var cachePath = _adapter.GetSwaggerCachePath(versionEntry.Version, definition.Name);
        if (!refresh && File.Exists(cachePath))
        {
            return cachePath;
        }

        var payload = prefetchedPayload;
        if (string.IsNullOrWhiteSpace(payload))
        {
            var swaggerUri = SwaggerUriResolver.Resolve(versionEntry,definition, environment);
            payload = await SwaggerDownloader.DownloadAsync(swaggerUri);
        }

        var directory = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(cachePath, payload);
        return cachePath;
    }

    private static bool IsGenericApiName(string apiName)
    {
        if (string.IsNullOrWhiteSpace(apiName))
        {
            return true;
        }

        if (apiName.Equals("api", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Regex.IsMatch(apiName, @"^api[-_ ]?\d+$", RegexOptions.IgnoreCase);
    }

    private static string? TryExtractOpenApiTitle(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!document.RootElement.TryGetProperty("info", out var infoEl) ||
                infoEl.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!infoEl.TryGetProperty("title", out var titleEl) ||
                titleEl.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return titleEl.GetString();
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizeApiName(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var normalized = title.Trim();
        normalized = Regex.Replace(normalized, @"\s*[|:-]\s*v?\d+(?:\.\d+)*\s*$", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\s+", ".");
        normalized = Regex.Replace(normalized, @"[^A-Za-z0-9._-]", string.Empty);
        normalized = Regex.Replace(normalized, @"[.]{2,}", ".");
        normalized = normalized.Trim('.', '-', '_');

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static Dictionary<string, string> InferDefinitionBaseUrl(string swaggerUrl, string environment)
    {
        if (string.IsNullOrWhiteSpace(swaggerUrl) ||
            !Uri.TryCreate(swaggerUrl, UriKind.Absolute, out var swaggerUri))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var authority = swaggerUri.IsDefaultPort
            ? $"{swaggerUri.Scheme}://{swaggerUri.Host}"
            : $"{swaggerUri.Scheme}://{swaggerUri.Host}:{swaggerUri.Port}";

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [environment] = authority
        };
    }

    private static Dictionary<string, string> CreateDefaultBaseUrlMap()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["local"] = "http://localhost",
            ["pre"] = "https://pre.example.com",
            ["prod"] = "https://api.example.com"
        };
    }
}

/// <summary>
/// Downloads swagger cache files for one version from an existing api-catalog.json.
/// </summary>
[McpTool("refresh_swagger_cache_from_catalog", "Downloads swagger files from api-catalog definitions into cache (defaults: version=0.1, environment=local, refresh=true)", Category = "Generation", Level = "L1")]
public sealed class RefreshSwaggerCacheFromCatalogTool : IMcpTool
{
    private const string DefaultEnvironment = "local";
    private readonly SihServicesAdapter _adapter;

    public RefreshSwaggerCacheFromCatalogTool(SihServicesAdapter adapter)
    {
        _adapter = adapter;
    }

    public string Name => "refresh_swagger_cache_from_catalog";
    public string Description => "Refreshes swagger cache for a catalog version (all definitions or selected apiNames). Defaults: version=0.1, environment=local, refresh=true.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            version = new { type = "string", description = "Catalog version to process (default: 0.1)" },
            environment = new { type = "string", description = "Environment key for relative swaggerUrl (default: local)" },
            refresh = new { type = "boolean", description = "Force redownload even if cache exists (default: true)" },
            apiNames = new
            {
                type = "array",
                description = "Optional subset of definition names",
                items = new { type = "string" }
            }
        }
    };

    public async Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var version = arguments?.GetValueOrDefault("version")?.ToString() ?? "0.1";
        var environment = arguments?.GetValueOrDefault("environment")?.ToString() ?? DefaultEnvironment;
        var refresh = ToolArgumentParser.TryReadBool(arguments, "refresh", true);
        var apiNames = ParseApiNames(arguments);

        if (!File.Exists(_adapter.ApiCatalogPath))
        {
            throw new FileNotFoundException($"Catalog file not found: {_adapter.ApiCatalogPath}");
        }

        var json = await File.ReadAllTextAsync(_adapter.ApiCatalogPath);
        var catalog = JsonSerializer.Deserialize<List<ApiCatalogVersion>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];

        var versionEntry = catalog.FirstOrDefault(v =>
            v.Version.Equals(version, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Catalog version not found: {version}");

        var definitions = apiNames.Count == 0
            ? versionEntry.Definitions
            : versionEntry.Definitions.Where(d => apiNames.Contains(d.Name)).ToList();

        var downloaded = new List<string>();
        var skipped = new List<string>();
        var failed = new List<object>();
        var renamedDefinitions = new List<object>();

        foreach (var definition in definitions)
        {
            try
            {
                var originalName = definition.Name;
                var existingCachePath = _adapter.GetSwaggerCachePath(versionEntry.Version, definition.Name);
                if (!refresh && File.Exists(existingCachePath))
                {
                    skipped.Add(definition.Name);
                    continue;
                }

                var swaggerUri = SwaggerUriResolver.Resolve(versionEntry,definition, environment);
                var payload = await SwaggerDownloader.DownloadAsync(swaggerUri);
                if (IsGenericApiName(definition.Name))
                {
                    var inferredName = NormalizeApiName(TryExtractOpenApiTitle(payload));
                    if (!string.IsNullOrWhiteSpace(inferredName) &&
                        !inferredName.Equals(definition.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        var hasCollision = versionEntry.Definitions.Any(x =>
                            !ReferenceEquals(x, definition) &&
                            x.Name.Equals(inferredName, StringComparison.OrdinalIgnoreCase));

                        if (!hasCollision)
                        {
                            definition.Name = inferredName;
                            renamedDefinitions.Add(new
                            {
                                previousName = originalName,
                                apiName = inferredName
                            });
                        }
                    }
                }

                var cachePath = _adapter.GetSwaggerCachePath(versionEntry.Version, definition.Name);

                var directory = Path.GetDirectoryName(cachePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(cachePath, payload);
                downloaded.Add(cachePath);
            }
            catch (Exception ex)
            {
                failed.Add(new
                {
                    apiName = definition.Name,
                    error = ex.Message
                });
            }
        }

        if (renamedDefinitions.Count > 0)
        {
            var updatedCatalogJson = JsonSerializer.Serialize(catalog, CreateCatalogJsonOptions());
            await File.WriteAllTextAsync(_adapter.ApiCatalogPath, updatedCatalogJson);
        }

        return new
        {
            version = versionEntry.Version,
            environment,
            refresh,
            catalogPath = _adapter.ApiCatalogPath,
            cacheRoot = _adapter.CachePath,
            selectedDefinitions = definitions.Select(d => d.Name).ToList(),
            downloaded,
            skipped,
            failed,
            renamedDefinitions,
            counts = new
            {
                selected = definitions.Count,
                downloaded = downloaded.Count,
                skipped = skipped.Count,
                failed = failed.Count
            }
        };
    }

    private static HashSet<string> ParseApiNames(Dictionary<string, object>? arguments)
    {
        if (arguments?.TryGetValue("apiNames", out var apiNamesObj) != true)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        if (apiNamesObj is not JsonElement apiNamesEl || apiNamesEl.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("apiNames must be an array");
        }

        return apiNamesEl
            .EnumerateArray()
            .Select(x => x.GetString() ?? string.Empty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsGenericApiName(string apiName)
    {
        if (string.IsNullOrWhiteSpace(apiName))
        {
            return true;
        }

        if (apiName.Equals("api", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Regex.IsMatch(apiName, @"^api[-_ ]?\d+$", RegexOptions.IgnoreCase);
    }

    private static string? TryExtractOpenApiTitle(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!document.RootElement.TryGetProperty("info", out var infoEl) ||
                infoEl.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!infoEl.TryGetProperty("title", out var titleEl) ||
                titleEl.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return titleEl.GetString();
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizeApiName(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var normalized = title.Trim();
        normalized = Regex.Replace(normalized, @"\s*[|:-]\s*v?\d+(?:\.\d+)*\s*$", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\s+", ".");
        normalized = Regex.Replace(normalized, @"[^A-Za-z0-9._-]", string.Empty);
        normalized = Regex.Replace(normalized, @"[.]{2,}", ".");
        normalized = normalized.Trim('.', '-', '_');

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static JsonSerializerOptions CreateCatalogJsonOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }
}

