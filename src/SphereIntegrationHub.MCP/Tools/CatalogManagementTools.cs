using System.Text.RegularExpressions;
using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.MCP.Core;
using SphereIntegrationHub.MCP.Services.Catalog;
using SphereIntegrationHub.MCP.Services.Integration;
using System.Text.Json;
using YamlDotNet.Serialization;

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
        IReadOnlyDictionary<string, string> definitionBaseUrls,
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

        if (!TryResolveTemplateEnvironment(definitionBaseUrls, preferredEnvironment, swaggerUri, out var environmentKey))
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
        IReadOnlyDictionary<string, string> definitionBaseUrls,
        string preferredEnvironment,
        Uri swaggerUri,
        out string environmentKey)
    {
        if (TryMatchEnvironment(definitionBaseUrls, preferredEnvironment, swaggerUri, out environmentKey))
        {
            return true;
        }

        foreach (var pair in definitionBaseUrls)
        {
            if (TryMatchEnvironment(definitionBaseUrls, pair.Key, swaggerUri, out environmentKey))
            {
                return true;
            }
        }

        environmentKey = string.Empty;
        return false;
    }

    private static bool TryMatchEnvironment(
        IReadOnlyDictionary<string, string> definitionBaseUrls,
        string environment,
        Uri swaggerUri,
        out string environmentKey)
    {
        environmentKey = string.Empty;
        if (!definitionBaseUrls.TryGetValue(environment, out var baseUrl) || string.IsNullOrWhiteSpace(baseUrl))
        {
            foreach (var pair in definitionBaseUrls)
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
[McpTool("generate_api_catalog_file", "Generates API catalog content and can write it to disk", Category = "Generation", Level = "L1")]
public sealed class GenerateApiCatalogFileTool : IMcpTool
{
    private readonly SihServicesAdapter _adapter;

    public GenerateApiCatalogFileTool(SihServicesAdapter adapter)
    {
        _adapter = adapter;
    }

    public string Name => "generate_api_catalog_file";
    public string Description => "Creates an API catalog for new projects. Writes YAML by default and also supports JSON output paths.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            versions = new
            {
                type = "array",
                description = "Catalog versions and definitions",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        version = new { type = "string" },
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
                                    healthCheck = new { type = "string" },
                                    readiness = new
                                    {
                                        type = "object",
                                        properties = new
                                        {
                                            maxRetries = new { type = "integer" },
                                            delayMs = new { type = "integer" },
                                            timeoutMs = new { type = "integer" },
                                            httpStatus = new
                                            {
                                                type = "array",
                                                items = new { type = "integer" }
                                            }
                                        }
                                    },
                                    baseUrl = new
                                    {
                                        type = "object",
                                        additionalProperties = new { type = "string" },
                                        description = "Optional per-definition baseUrl map keyed by environment."
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

            if (!versionItem.TryGetProperty("definitions", out var defsEl) || defsEl.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException("Each versions item must include 'definitions' array");
            }

            var versionBaseUrl = ParseBaseUrlFromElement(versionItem);

            var definitions = new List<ApiDefinition>();
            foreach (var definitionItem in defsEl.EnumerateArray())
            {
                var name = definitionItem.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                var port = definitionItem.TryGetProperty("port", out var portEl) && portEl.TryGetInt32(out var parsedPort)
                    ? parsedPort
                    : (int?)null;
                var basePath = definitionItem.TryGetProperty("basePath", out var basePathEl) ? basePathEl.GetString() : null;
                var swaggerUrl = definitionItem.TryGetProperty("swaggerUrl", out var swaggerEl) ? swaggerEl.GetString() : null;
                var healthCheck = definitionItem.TryGetProperty("healthCheck", out var healthCheckEl) ? healthCheckEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(name) ||
                    string.IsNullOrWhiteSpace(basePath) ||
                    string.IsNullOrWhiteSpace(swaggerUrl))
                {
                    throw new ArgumentException("Definition requires name, basePath, swaggerUrl");
                }

                var normalizedSwaggerUrl = CatalogSwaggerUrlNormalizer.NormalizeForCatalog(swaggerUrl!);
                var definitionBaseUrl = ParseDefinitionBaseUrl(definitionItem);
                var readiness = ParseReadinessFromElement(definitionItem);

                // Use definition-level baseUrl if present; fall back to version-level baseUrl.
                var effectiveBaseUrl = definitionBaseUrl.Count > 0 ? definitionBaseUrl : versionBaseUrl;

                if (effectiveBaseUrl.Count > 0 &&
                    !normalizedSwaggerUrl.Contains("{{baseUrl.", StringComparison.OrdinalIgnoreCase) &&
                    CatalogSwaggerTemplateBuilder.TryBuildTemplate(
                        normalizedSwaggerUrl,
                        effectiveBaseUrl,
                        effectiveBaseUrl.Keys.FirstOrDefault(static key =>
                            key.Equals("local", StringComparison.OrdinalIgnoreCase)) ?? "local",
                        out var templatedSwaggerUrl,
                        out var inferredPort))
                {
                    normalizedSwaggerUrl = templatedSwaggerUrl;
                    port ??= inferredPort;
                    // Store the effective baseUrl on the definition so the catalog is self-contained.
                    if (definitionBaseUrl.Count == 0)
                    {
                        definitionBaseUrl = effectiveBaseUrl;
                    }
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
                    HealthCheck = healthCheck,
                    Readiness = readiness,
                    BaseUrl = definitionBaseUrl.Count > 0 ? definitionBaseUrl : null
                });
            }

            versions.Add(new ApiCatalogVersion
            {
                Version = version!,
                Definitions = definitions
            });
        }

        var writeToDisk = ToolArgumentParser.TryReadBool(arguments, "writeToDisk", true);
        var outputPath = arguments?.GetValueOrDefault("outputPath")?.ToString();
        outputPath = ResolveOutputPath(outputPath);
        var catalogContent = ApiCatalogFile.Serialize(versions, outputPath);

        if (writeToDisk)
        {
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(outputPath, catalogContent);
        }

        return Task.FromResult<object>(new
        {
            outputPath,
            writeToDisk,
            catalogContent,
            catalogFormat = ApiCatalogFile.GetFormat(outputPath).ToString().ToLowerInvariant(),
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
        => ParseBaseUrlFromElement(definitionItem);

    private static ApiReadinessPolicyDefinition? ParseReadinessFromElement(JsonElement element)
    {
        if (!element.TryGetProperty("readiness", out var readinessEl) ||
            readinessEl.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ParseReadiness(readinessEl);
    }

    private static ApiReadinessPolicyDefinition? ParseReadiness(JsonElement readinessEl)
    {
        var hasValue = false;
        var readiness = new ApiReadinessPolicyDefinition();

        if (readinessEl.TryGetProperty("maxRetries", out var maxRetriesEl) && maxRetriesEl.TryGetInt32(out var maxRetries))
        {
            readiness.MaxRetries = maxRetries;
            hasValue = true;
        }

        if (readinessEl.TryGetProperty("delayMs", out var delayMsEl) && delayMsEl.TryGetInt32(out var delayMs))
        {
            readiness.DelayMs = delayMs;
            hasValue = true;
        }

        if (readinessEl.TryGetProperty("timeoutMs", out var timeoutMsEl) && timeoutMsEl.TryGetInt32(out var timeoutMs))
        {
            readiness.TimeoutMs = timeoutMs;
            hasValue = true;
        }

        if (readinessEl.TryGetProperty("httpStatus", out var httpStatusEl) &&
            httpStatusEl.ValueKind == JsonValueKind.Array)
        {
            readiness.HttpStatus = httpStatusEl
                .EnumerateArray()
                .Where(static item => item.TryGetInt32(out _))
                .Select(static item => item.GetInt32())
                .ToArray();
            hasValue = true;
        }

        return hasValue ? readiness : null;
    }

    private static Dictionary<string, string> ParseBaseUrlFromElement(JsonElement element)
    {
        var baseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (element.TryGetProperty("baseUrl", out var baseUrlEl) &&
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

}

/// <summary>
/// Migrates an existing catalog file to another supported path/format without changing its data.
/// </summary>
[McpTool("migrate_api_catalog", "Migrates an existing API catalog to another path/format", Category = "Generation", Level = "L1")]
public sealed class MigrateApiCatalogTool : IMcpTool
{
    private readonly SihServicesAdapter _adapter;
    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder().Build();
    private static readonly ISerializer YamlSerializer = new SerializerBuilder()
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    public MigrateApiCatalogTool(SihServicesAdapter adapter)
    {
        _adapter = adapter;
    }

    public string Name => "migrate_api_catalog";
    public string Description => "Loads an existing API catalog and rewrites it to another supported path such as api.catalog.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            sourcePath = new { type = "string", description = "Source catalog path. Defaults to configured catalog path." },
            outputPath = new { type = "string", description = "Target catalog path. Defaults to configured catalog path." },
            writeToDisk = new { type = "boolean", description = "Write the migrated catalog to disk (default: true)." }
        }
    };

    public async Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var sourcePath = ResolvePath(arguments?.GetValueOrDefault("sourcePath")?.ToString() ?? _adapter.ApiCatalogPath);
        var outputPath = ResolvePath(arguments?.GetValueOrDefault("outputPath")?.ToString() ?? _adapter.ApiCatalogPath);
        var writeToDisk = ToolArgumentParser.TryReadBool(arguments, "writeToDisk", true);

        var sourceFormat = ApiCatalogFile.GetFormat(sourcePath);
        var outputFormat = ApiCatalogFile.GetFormat(outputPath);
        var sourceContent = await File.ReadAllTextAsync(sourcePath);
        var document = DeserializeCatalogDocument(sourceContent, sourceFormat);
        var content = SerializeCatalogDocument(document, outputFormat);

        if (writeToDisk)
        {
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(outputPath, content);
        }

        return new
        {
            sourcePath,
            outputPath,
            sourceFormat = sourceFormat.ToString().ToLowerInvariant(),
            outputFormat = outputFormat.ToString().ToLowerInvariant(),
            writeToDisk,
            versionsCount = CountVersions(document),
            catalogContent = content
        };
    }

    private string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(Path.Combine(_adapter.ProjectRoot, path));
    }

    private static object? DeserializeCatalogDocument(string content, ApiCatalogFormat format)
    {
        return format switch
        {
            ApiCatalogFormat.Json => DeserializeJsonDocument(content),
            ApiCatalogFormat.Yaml => YamlDeserializer.Deserialize<object>(content),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported catalog format.")
        };
    }

    private static string SerializeCatalogDocument(object? document, ApiCatalogFormat format)
    {
        return format switch
        {
            ApiCatalogFormat.Json => JsonSerializer.Serialize(document, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            }),
            ApiCatalogFormat.Yaml => YamlSerializer.Serialize(document),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported catalog format.")
        };
    }

    private static object? DeserializeJsonDocument(string content)
    {
        using var document = JsonDocument.Parse(content);
        return ConvertJsonElement(document.RootElement);
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(property => property.Name, property => ConvertJsonElement(property.Value)),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(ConvertJsonElement)
                .ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }

    private static int CountVersions(object? document)
    {
        return document switch
        {
            IList<object> list => list.Count,
            _ => 0
        };
    }
}

/// <summary>
/// Creates or updates a single API definition in the API catalog and optionally downloads swagger cache.
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
    public string Description => "Upserts one definition into the API catalog (create if missing) and downloads swagger cache for it.";

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
            healthCheck = new { type = "string", description = "Optional health check path (e.g. /health)" },
            readiness = new
            {
                type = "object",
                description = "Optional readiness retry/timeout policy applied to health checks and swagger fetches.",
                properties = new
                {
                    maxRetries = new { type = "integer" },
                    delayMs = new { type = "integer" },
                    timeoutMs = new { type = "integer" },
                    httpStatus = new
                    {
                        type = "array",
                        description = "Optional HTTP status codes accepted as healthy for healthCheck probes.",
                        items = new { type = "integer" }
                    }
                }
            },
            environment = new { type = "string", description = "Environment key to resolve relative swaggerUrl (default: pre)" },
            baseUrl = new
            {
                type = "object",
                description = "Per-definition baseUrl map keyed by environment."
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
        var healthCheck = arguments?.GetValueOrDefault("healthCheck")?.ToString();
        var readiness = ParseReadiness(arguments);
        var environment = arguments?.GetValueOrDefault("environment")?.ToString() ?? DefaultEnvironment;
        var downloadCache = ToolArgumentParser.TryReadBool(arguments, "downloadCache", true);
        var overwriteDefinition = ToolArgumentParser.TryReadBool(arguments, "overwriteDefinition", true);
        var requestedApiName = apiName;

        var definitionBaseUrlOverrides = ParseBaseUrlMap(arguments);

        var (catalog, catalogCreated) = await LoadCatalogAsync(_adapter.ApiCatalogPath);
        var versionEntry = catalog.FirstOrDefault(v =>
            v.Version.Equals(version, StringComparison.OrdinalIgnoreCase));

        var versionCreated = false;
        if (versionEntry == null)
        {
            versionEntry = new ApiCatalogVersion
            {
                Version = version,
                Definitions = []
            };
            catalog.Add(versionEntry);
            versionCreated = true;
        }

        var basePathValue = string.IsNullOrWhiteSpace(basePath) ? "/" : basePath;
        var storedSwaggerUrl = swaggerUrl;
        var storedPort = port;
        if (CatalogSwaggerTemplateBuilder.TryBuildTemplate(
                swaggerUrl,
                definitionBaseUrlOverrides,
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
                    BaseUrl = MergeBaseUrls(
                        InferDefinitionBaseUrl(swaggerUrl, environment),
                        definitionBaseUrlOverrides)
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
                HealthCheck = healthCheck,
                Readiness = readiness,
                BaseUrl = MergeBaseUrls(
                    InferDefinitionBaseUrl(swaggerUrl, environment),
                    definitionBaseUrlOverrides)
            };
            versionEntry.Definitions.Add(definition);
            definitionAction = "created";
        }
        else if (overwriteDefinition)
        {
            definition.BasePath = basePathValue;
            definition.SwaggerUrl = storedSwaggerUrl;
            definition.Port = storedPort;
            if (!string.IsNullOrWhiteSpace(healthCheck))
            {
                definition.HealthCheck = healthCheck;
            }
            if (readiness is not null)
            {
                definition.Readiness = readiness;
            }

            var mergedBaseUrl = MergeBaseUrls(
                InferDefinitionBaseUrl(swaggerUrl, environment),
                definitionBaseUrlOverrides,
                definition.BaseUrl);
            if (mergedBaseUrl.Count > 0)
            {
                definition.BaseUrl = mergedBaseUrl;
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

    private static ApiReadinessPolicyDefinition? ParseReadiness(Dictionary<string, object>? arguments)
    {
        if (arguments?.TryGetValue("readiness", out var readinessObj) != true)
        {
            return null;
        }

        if (readinessObj is not JsonElement readinessEl || readinessEl.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("readiness must be an object");
        }

        var hasValue = false;
        var readiness = new ApiReadinessPolicyDefinition();

        if (readinessEl.TryGetProperty("maxRetries", out var maxRetriesEl) && maxRetriesEl.TryGetInt32(out var maxRetries))
        {
            readiness.MaxRetries = maxRetries;
            hasValue = true;
        }

        if (readinessEl.TryGetProperty("delayMs", out var delayMsEl) && delayMsEl.TryGetInt32(out var delayMs))
        {
            readiness.DelayMs = delayMs;
            hasValue = true;
        }

        if (readinessEl.TryGetProperty("timeoutMs", out var timeoutMsEl) && timeoutMsEl.TryGetInt32(out var timeoutMs))
        {
            readiness.TimeoutMs = timeoutMs;
            hasValue = true;
        }

        if (readinessEl.TryGetProperty("httpStatus", out var httpStatusEl) &&
            httpStatusEl.ValueKind == JsonValueKind.Array)
        {
            readiness.HttpStatus = httpStatusEl
                .EnumerateArray()
                .Where(static item => item.TryGetInt32(out _))
                .Select(static item => item.GetInt32())
                .ToArray();
            hasValue = true;
        }

        return hasValue ? readiness : null;
    }

    private static async Task<(List<ApiCatalogVersion> Catalog, bool Created)> LoadCatalogAsync(string catalogPath)
    {
        if (!File.Exists(catalogPath))
        {
            return ([], true);
        }

        return ((await ApiCatalogFile.LoadAsync(catalogPath)).ToList(), false);
    }

    private static async Task SaveCatalogAsync(string catalogPath, List<ApiCatalogVersion> catalog)
    {
        await ApiCatalogFile.SaveAsync(catalogPath, catalog);
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

    private static Dictionary<string, string> MergeBaseUrls(params IReadOnlyDictionary<string, string>?[] maps)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var map in maps)
        {
            if (map == null)
            {
                continue;
            }

            foreach (var pair in map)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) &&
                    !string.IsNullOrWhiteSpace(pair.Value))
                {
                    merged[pair.Key] = pair.Value;
                }
            }
        }

        return merged;
    }

}

/// <summary>
/// Downloads swagger cache files for one version from an existing API catalog.
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

        var catalog = (await ApiCatalogFile.LoadAsync(_adapter.ApiCatalogPath)).ToList();

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
            await ApiCatalogFile.SaveAsync(_adapter.ApiCatalogPath, catalog);
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

}
