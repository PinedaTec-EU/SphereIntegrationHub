using System.Text.RegularExpressions;
using SphereIntegrationHub.MCP.Core;
using SphereIntegrationHub.MCP.Models;
using SphereIntegrationHub.MCP.Services.Catalog;
using SphereIntegrationHub.MCP.Services.Generation;
using SphereIntegrationHub.MCP.Services.Integration;
using SphereIntegrationHub.MCP.Services;
using SphereIntegrationHub.MCP.Services.Validation;
using System.Text.Json;
using WorkflowDefinition = SphereIntegrationHub.Definitions.WorkflowDefinition;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

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
/// Generates a workflow stage from an API endpoint
/// </summary>
[McpTool("generate_endpoint_stage", "Generates a workflow stage definition from an API endpoint", Category = "Generation", Level = "L1")]
public sealed class GenerateEndpointStageTool : IMcpTool
{
    private readonly IStageGenerator _generator;

    public GenerateEndpointStageTool(SihServicesAdapter adapter)
    {
        _generator = new StageGenerator(adapter);
    }

    public string Name => "generate_endpoint_stage";
    public string Description => "Generates a SphereIntegrationHub endpoint stage. If swagger cache is missing, provide endpointSchema.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            version = new { type = "string", description = "API catalog version" },
            apiName = new { type = "string", description = "API name/reference" },
            endpoint = new { type = "string", description = "Endpoint path" },
            httpVerb = new { type = "string", description = "HTTP verb" },
            stageName = new { type = "string", description = "Optional custom stage name" },
            endpointSchema = new
            {
                type = "object",
                description = "Optional fallback endpoint schema when swagger cache is unavailable"
            }
        }
    };

    public async Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var stageName = arguments?.GetValueOrDefault("stageName")?.ToString();
        var fallbackEndpoint = ToolArgumentParser.TryParseEndpointSchema(arguments, false);

        var version = arguments?.GetValueOrDefault("version")?.ToString();
        var apiName = arguments?.GetValueOrDefault("apiName")?.ToString() ?? fallbackEndpoint?.ApiName;
        var endpoint = arguments?.GetValueOrDefault("endpoint")?.ToString() ?? fallbackEndpoint?.Endpoint;
        var httpVerb = arguments?.GetValueOrDefault("httpVerb")?.ToString() ?? fallbackEndpoint?.HttpVerb;

        if (fallbackEndpoint == null)
        {
            if (string.IsNullOrWhiteSpace(version) ||
                string.IsNullOrWhiteSpace(apiName) ||
                string.IsNullOrWhiteSpace(endpoint) ||
                string.IsNullOrWhiteSpace(httpVerb))
            {
                throw new ArgumentException(
                    "Provide either (version, apiName, endpoint, httpVerb) or endpointSchema.");
            }
        }

        var yaml = await _generator.GenerateEndpointStageAsync(
            version ?? "unknown",
            apiName!,
            endpoint!,
            httpVerb!,
            stageName,
            fallbackEndpoint);

        return new
        {
            stageName = stageName ?? "generated-stage",
            yaml,
            source = fallbackEndpoint == null ? "swagger-cache" : "endpoint-schema-fallback"
        };
    }

}

/// <summary>
/// Generates a workflow skeleton
/// </summary>
[McpTool("generate_workflow_skeleton", "Generates a complete workflow skeleton with basic structure", Category = "Generation", Level = "L1")]
public sealed class GenerateWorkflowSkeletonTool : IMcpTool
{
    private readonly IStageGenerator _generator;
    private readonly ApiCatalogReader _catalogReader;

    public GenerateWorkflowSkeletonTool(SihServicesAdapter adapter)
    {
        _generator = new StageGenerator(adapter);
        _catalogReader = new ApiCatalogReader(adapter);
    }

    public string Name => "generate_workflow_skeleton";
    public string Description => "Generates a complete workflow skeleton aligned with SphereIntegrationHub schema";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            name = new { type = "string", description = "Workflow name" },
            description = new { type = "string", description = "Workflow description" },
            version = new { type = "string", description = "Catalog version (optional: falls back to first catalog version)" },
            inputParameters = new
            {
                type = "array",
                description = "Input parameter names",
                items = new { type = "string" }
            }
        },
        required = new[] { "name", "description" }
    };

    public async Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var name = arguments?.GetValueOrDefault("name")?.ToString()
            ?? throw new ArgumentException("name is required");
        var description = arguments?.GetValueOrDefault("description")?.ToString()
            ?? throw new ArgumentException("description is required");
        var warningMessages = new List<string>();
        var version = await VersionResolver.ResolveAsync(arguments?.GetValueOrDefault("version")?.ToString(), _catalogReader, warningMessages);

        List<string> inputParameters = [];
        if (arguments?.TryGetValue("inputParameters", out var inputParamsObj) == true)
        {
            if (inputParamsObj is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Array)
            {
                inputParameters = JsonSerializer.Deserialize<List<string>>(jsonElement.GetRawText()) ?? [];
            }
            else if (inputParamsObj is List<object> list)
            {
                inputParameters = list
                    .Select(item => item.ToString() ?? string.Empty)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .ToList();
            }
        }

        var yaml = _generator.GenerateWorkflowSkeleton(name, description, inputParameters, version);
        var wfvars = WorkflowArtifactHelper.GenerateWfvars(yaml);
        return new
        {
            name,
            version,
            yaml,
            wfvars,
            warnings = warningMessages
        };
    }
}

/// <summary>
/// Generates a mock payload for an endpoint
/// </summary>
[McpTool("generate_mock_payload", "Generates a mock JSON payload for an API endpoint", Category = "Generation", Level = "L1")]
public sealed class GenerateMockPayloadTool : IMcpTool
{
    private readonly IStageGenerator _generator;

    public GenerateMockPayloadTool(SihServicesAdapter adapter)
    {
        _generator = new StageGenerator(adapter);
    }

    public string Name => "generate_mock_payload";
    public string Description => "Generates a mock JSON payload. If swagger cache is missing, provide endpointSchema.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            version = new { type = "string", description = "API catalog version" },
            apiName = new { type = "string", description = "API name" },
            endpoint = new { type = "string", description = "Endpoint path" },
            httpVerb = new { type = "string", description = "HTTP verb" },
            endpointSchema = new { type = "object", description = "Optional fallback endpoint schema" }
        }
    };

    public async Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var fallbackEndpoint = ToolArgumentParser.TryParseEndpointSchema(arguments, false);
        var version = arguments?.GetValueOrDefault("version")?.ToString();
        var apiName = arguments?.GetValueOrDefault("apiName")?.ToString() ?? fallbackEndpoint?.ApiName;
        var endpoint = arguments?.GetValueOrDefault("endpoint")?.ToString() ?? fallbackEndpoint?.Endpoint;
        var httpVerb = arguments?.GetValueOrDefault("httpVerb")?.ToString() ?? fallbackEndpoint?.HttpVerb;

        if (fallbackEndpoint == null)
        {
            if (string.IsNullOrWhiteSpace(version) ||
                string.IsNullOrWhiteSpace(apiName) ||
                string.IsNullOrWhiteSpace(endpoint) ||
                string.IsNullOrWhiteSpace(httpVerb))
            {
                throw new ArgumentException(
                    "Provide either (version, apiName, endpoint, httpVerb) or endpointSchema.");
            }
        }

        var payload = await _generator.GenerateMockPayloadAsync(
            version ?? "unknown",
            apiName!,
            endpoint!,
            httpVerb!,
            fallbackEndpoint);

        return new
        {
            endpoint,
            httpVerb,
            payload,
            source = fallbackEndpoint == null ? "swagger-cache" : "endpoint-schema-fallback"
        };
    }
}

/// <summary>
/// Generates aligned workflow and wfvars drafts from endpoint list.
/// </summary>
[McpTool("generate_workflow_bundle", "Generates workflow YAML + wfvars draft + payload templates", Category = "Generation", Level = "L1")]
public sealed class GenerateWorkflowBundleTool : IMcpTool
{
    private readonly IStageGenerator _generator;
    private readonly ApiCatalogReader _catalogReader;

    public GenerateWorkflowBundleTool(SihServicesAdapter adapter)
    {
        _generator = new StageGenerator(adapter);
        _catalogReader = new ApiCatalogReader(adapter);
    }

    public string Name => "generate_workflow_bundle";
    public string Description => "Creates a complete bundle for automation: workflow draft, wfvars draft, and payload templates.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            version = new { type = "string", description = "Catalog version (optional: falls back to first catalog version)" },
            workflowName = new { type = "string", description = "Workflow name" },
            description = new { type = "string", description = "Workflow description" },
            apiName = new { type = "string", description = "Default apiRef/definition name" },
            endpoints = new
            {
                type = "array",
                description = "Ordered endpoint definitions",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        stageName = new { type = "string" },
                        endpoint = new { type = "string" },
                        httpVerb = new { type = "string" },
                        apiName = new { type = "string" },
                        endpointSchema = new { type = "object" }
                    }
                }
            }
        },
        required = new[] { "workflowName", "apiName", "endpoints" }
    };

    public async Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var warningMessages = new List<string>();
        var version = await VersionResolver.ResolveAsync(arguments?.GetValueOrDefault("version")?.ToString(), _catalogReader, warningMessages);
        var workflowName = arguments?.GetValueOrDefault("workflowName")?.ToString()
            ?? throw new ArgumentException("workflowName is required");
        var description = arguments?.GetValueOrDefault("description")?.ToString() ?? $"Workflow generated for {workflowName}";
        var apiName = arguments?.GetValueOrDefault("apiName")?.ToString()
            ?? throw new ArgumentException("apiName is required");

        if (arguments?.TryGetValue("endpoints", out var endpointsObj) != true ||
            endpointsObj is not JsonElement endpointsEl ||
            endpointsEl.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("endpoints is required and must be an array");
        }

        var stages = new List<Dictionary<string, object?>>();
        var payloads = new List<object>();
        var inputNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var endpointItem in endpointsEl.EnumerateArray())
        {
            var endpointSchema = endpointItem.TryGetProperty("endpointSchema", out var endpointSchemaEl)
                ? new Dictionary<string, object> { ["endpointSchema"] = endpointSchemaEl }
                : null;

            var fallbackEndpoint = endpointSchema == null
                ? null
                : ToolArgumentParser.TryParseEndpointSchema(endpointSchema, true);

            var itemApiName = endpointItem.TryGetProperty("apiName", out var itemApiEl)
                ? itemApiEl.GetString()
                : apiName;
            var endpoint = endpointItem.TryGetProperty("endpoint", out var endpointEl) ? endpointEl.GetString() : fallbackEndpoint?.Endpoint;
            var httpVerb = endpointItem.TryGetProperty("httpVerb", out var httpVerbEl) ? httpVerbEl.GetString() : fallbackEndpoint?.HttpVerb;
            var stageName = endpointItem.TryGetProperty("stageName", out var stageNameEl) ? stageNameEl.GetString() : null;

            EndpointInfo endpointInfo;
            if (fallbackEndpoint != null)
            {
                endpointInfo = fallbackEndpoint with { ApiName = itemApiName ?? fallbackEndpoint.ApiName };
            }
            else
            {
                if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(httpVerb) || string.IsNullOrWhiteSpace(itemApiName))
                {
                    throw new ArgumentException(
                        "Each endpoint requires apiName/endpoint/httpVerb or endpointSchema.");
                }

                var yaml = await _generator.GenerateEndpointStageAsync(
                    version,
                    itemApiName!,
                    endpoint!,
                    httpVerb!,
                    stageName);

                var stage = DeserializeYamlMap(yaml);
                stages.Add(stage);
                CollectInputTokens(stage, inputNames);

                if (httpVerb is "POST" or "PUT" or "PATCH")
                {
                    var payload = await _generator.GenerateMockPayloadAsync(version, itemApiName!, endpoint!, httpVerb!);
                    payloads.Add(new
                    {
                        stageName = stage["name"],
                        endpoint,
                        httpVerb,
                        payload
                    });
                }

                continue;
            }

            var parsedStageName = stageName ?? endpointInfo.Endpoint.Replace("/", "_").Trim('_').ToLowerInvariant();
            var stageDict = _generator.BuildEndpointStage(endpointInfo, parsedStageName);
            stages.Add(stageDict);
            CollectInputTokens(stageDict, inputNames);

            if (endpointInfo.HttpVerb is "POST" or "PUT" or "PATCH")
            {
                var payload = await _generator.GenerateMockPayloadAsync(
                    version,
                    endpointInfo.ApiName,
                    endpointInfo.Endpoint,
                    endpointInfo.HttpVerb,
                    endpointInfo);

                payloads.Add(new
                {
                    stageName = parsedStageName,
                    endpoint = endpointInfo.Endpoint,
                    httpVerb = endpointInfo.HttpVerb,
                    payload
                });
            }
        }

        if (inputNames.Count == 0)
        {
            inputNames.Add("username");
            inputNames.Add("password");
        }

        var workflowDraft = _generator.GenerateWorkflowBundle(
            workflowName,
            description,
            version,
            apiName,
            stages,
            inputNames);

        var wfvars = _generator.GenerateWfvars(inputNames);

        return new
        {
            version,
            workflowDraft,
            wfvars,
            payloadDrafts = payloads,
            warnings = warningMessages
        };
    }

    private static Dictionary<string, object?> DeserializeYamlMap(string yaml)
    {
        var deserializer = new YamlDotNet.Serialization.DeserializerBuilder().Build();
        var map = deserializer.Deserialize<Dictionary<string, object?>>(yaml);
        return map ?? [];
    }

    private static void CollectInputTokens(Dictionary<string, object?> node, HashSet<string> inputNames)
    {
        var inputRegex = new Regex(@"\{\{input\.([a-zA-Z0-9_]+)\}\}", RegexOptions.Compiled);

        void Walk(object? value)
        {
            if (value == null)
            {
                return;
            }

            if (value is string text)
            {
                foreach (Match match in inputRegex.Matches(text))
                {
                    inputNames.Add(match.Groups[1].Value);
                }
                return;
            }

            if (value is IDictionary<object, object> objDict)
            {
                foreach (var pair in objDict.Values)
                {
                    Walk(pair);
                }
                return;
            }

            if (value is IDictionary<string, object?> strDict)
            {
                foreach (var pair in strDict.Values)
                {
                    Walk(pair);
                }
                return;
            }

            if (value is IEnumerable<object> list)
            {
                foreach (var item in list)
                {
                    Walk(item);
                }
            }
        }

        Walk(node);
    }
}

/// <summary>
/// Persists generated workflow artifacts to disk.
/// </summary>
[McpTool("write_workflow_artifacts", "Writes workflow, wfvars, and optional payload files into configured workflows path", Category = "Generation", Level = "L1")]
public sealed class WriteWorkflowArtifactsTool : IMcpTool
{
    private readonly SihServicesAdapter _adapter;

    public WriteWorkflowArtifactsTool(SihServicesAdapter adapter)
    {
        _adapter = adapter;
    }

    public string Name => "write_workflow_artifacts";
    public string Description => "Persists generated artifacts in the target project. Paths are relative to SIH_WORKFLOWS_PATH unless absolute.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            workflowPath = new { type = "string", description = "Destination .workflow path" },
            workflowYaml = new { type = "string", description = "Workflow YAML content" },
            wfvarsPath = new { type = "string", description = "Destination .wfvars path (optional)" },
            wfvarsYaml = new { type = "string", description = ".wfvars YAML content (optional)" },
            payloadFiles = new
            {
                type = "array",
                description = "Optional payload files",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        path = new { type = "string" },
                        content = new { type = "string" }
                    },
                    required = new[] { "path", "content" }
                }
            }
        },
        required = new[] { "workflowPath", "workflowYaml" }
    };

    public Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var workflowPath = arguments?.GetValueOrDefault("workflowPath")?.ToString()
            ?? throw new ArgumentException("workflowPath is required");
        var workflowYaml = arguments?.GetValueOrDefault("workflowYaml")?.ToString()
            ?? throw new ArgumentException("workflowYaml is required");
        var wfvarsPath = arguments?.GetValueOrDefault("wfvarsPath")?.ToString();
        var wfvarsYaml = arguments?.GetValueOrDefault("wfvarsYaml")?.ToString();

        var writtenFiles = new List<string>();
        var warnings = new List<string>();
        string? wfvarsPathResolved = null;

        var resolvedWorkflowPath = ResolveWorkflowTargetPath(workflowPath, warnings);
        EnsureDirectory(resolvedWorkflowPath);
        File.WriteAllText(resolvedWorkflowPath, workflowYaml);
        writtenFiles.Add(resolvedWorkflowPath);

        if (!string.IsNullOrWhiteSpace(wfvarsYaml))
        {
            wfvarsPathResolved = ResolveWfvarsTargetPath(wfvarsPath, resolvedWorkflowPath, warnings);
            EnsureDirectory(wfvarsPathResolved);
            File.WriteAllText(wfvarsPathResolved, wfvarsYaml);
            writtenFiles.Add(wfvarsPathResolved);
        }

        if (arguments?.TryGetValue("payloadFiles", out var payloadFilesObj) == true &&
            payloadFilesObj is JsonElement payloadFilesEl &&
            payloadFilesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var payloadItem in payloadFilesEl.EnumerateArray())
            {
                if (!payloadItem.TryGetProperty("path", out var pathEl) ||
                    !payloadItem.TryGetProperty("content", out var contentEl))
                {
                    continue;
                }

                var targetPath = ResolveTargetPath(pathEl.GetString() ?? string.Empty);
                var content = contentEl.GetString() ?? string.Empty;
                EnsureDirectory(targetPath);
                File.WriteAllText(targetPath, content);
                writtenFiles.Add(targetPath);
            }
        }

        return Task.FromResult<object>(new
        {
            workflowsRoot = _adapter.WorkflowsPath,
            workflowPathResolved = resolvedWorkflowPath,
            wfvarsPathResolved,
            writtenFiles,
            count = writtenFiles.Count,
            warnings
        });
    }

    private string ResolveWorkflowTargetPath(string path, List<string> warnings)
    {
        var resolved = ResolveTargetPath(path);
        var normalized = NormalizeWorkflowFileExtension(resolved);
        if (!resolved.Equals(normalized, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"workflowPath '{path}' was normalized to '{normalized}'. Use '.workflow' extension for workflow files.");
        }

        return normalized;
    }

    private string ResolveWfvarsTargetPath(string? wfvarsPath, string resolvedWorkflowPath, List<string> warnings)
    {
        var resolved = string.IsNullOrWhiteSpace(wfvarsPath)
            ? Path.ChangeExtension(resolvedWorkflowPath, ".wfvars")
            : ResolveTargetPath(wfvarsPath);
        var normalized = NormalizeWfvarsFileExtension(resolved);
        if (!resolved.Equals(normalized, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"wfvarsPath '{wfvarsPath}' was normalized to '{normalized}'. Use '.wfvars' extension for vars files.");
        }

        return normalized;
    }

    private string ResolveTargetPath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(Path.Combine(_adapter.WorkflowsPath, path));
    }

    private static string NormalizeWorkflowFileExtension(string path)
    {
        var extension = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(extension) ||
            extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".yml", StringComparison.OrdinalIgnoreCase))
        {
            return Path.ChangeExtension(path, ".workflow");
        }

        return path;
    }

    private static string NormalizeWfvarsFileExtension(string path)
    {
        var extension = Path.GetExtension(path);
        if (!extension.Equals(".wfvars", StringComparison.OrdinalIgnoreCase))
        {
            return Path.ChangeExtension(path, ".wfvars");
        }

        return path;
    }

    private static void EnsureDirectory(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}

/// <summary>
/// Generates wfvars draft from an existing workflow definition.
/// </summary>
[McpTool("generate_wfvars_from_workflow", "Generates wfvars draft from workflow inputs and optionally writes it to disk", Category = "Generation", Level = "L1")]
public sealed class GenerateWfvarsFromWorkflowTool : IMcpTool
{
    private readonly SihServicesAdapter _adapter;

    public GenerateWfvarsFromWorkflowTool(SihServicesAdapter adapter)
    {
        _adapter = adapter;
    }

    public string Name => "generate_wfvars_from_workflow";
    public string Description => "Reads a workflow file, derives wfvars keys from input parameters, and optionally writes the .wfvars file.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            workflowPath = new { type = "string", description = "Workflow file path (.workflow/.yaml), absolute or relative to workflows root" },
            wfvarsPath = new { type = "string", description = "Optional wfvars output path; defaults to workflow sibling with .wfvars extension" },
            writeChanges = new { type = "boolean", description = "Write generated wfvars file to disk (default: true)" }
        },
        required = new[] { "workflowPath" }
    };

    public async Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var workflowPathArg = arguments?.GetValueOrDefault("workflowPath")?.ToString()
            ?? throw new ArgumentException("workflowPath is required");
        var wfvarsPathArg = arguments?.GetValueOrDefault("wfvarsPath")?.ToString();
        var writeChanges = ToolArgumentParser.TryReadBool(arguments, "writeChanges", true);

        var workflowPath = ResolvePath(workflowPathArg);
        if (!File.Exists(workflowPath))
        {
            throw new FileNotFoundException($"Workflow file not found: {workflowPath}", workflowPath);
        }

        var workflowYaml = await File.ReadAllTextAsync(workflowPath);
        var wfvars = WorkflowArtifactHelper.GenerateWfvars(workflowYaml);
        var resolvedWfvarsPath = ResolveWfvarsPath(workflowPath, wfvarsPathArg);
        var warnings = new List<string>();
        var written = false;

        if (string.IsNullOrWhiteSpace(wfvars))
        {
            warnings.Add("Workflow has no input variables; no wfvars content was generated.");
        }
        else if (writeChanges)
        {
            EnsureDirectory(resolvedWfvarsPath);
            await File.WriteAllTextAsync(resolvedWfvarsPath, wfvars);
            written = true;
        }

        return new
        {
            workflowPath,
            wfvarsPath = resolvedWfvarsPath,
            writeChanges,
            written,
            hasInputs = !string.IsNullOrWhiteSpace(wfvars),
            wfvars,
            warnings
        };
    }

    private string ResolvePath(string path)
    {
        return WorkflowPathResolver.ResolveExistingWorkflowPath(_adapter, path);
    }

    private static string ResolveWfvarsPath(string workflowPath, string? wfvarsPathArg)
    {
        if (!string.IsNullOrWhiteSpace(wfvarsPathArg))
        {
            if (Path.IsPathRooted(wfvarsPathArg))
            {
                return Path.GetFullPath(wfvarsPathArg);
            }

            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(workflowPath) ?? string.Empty, wfvarsPathArg));
        }

        return Path.ChangeExtension(workflowPath, ".wfvars");
    }

    private static void EnsureDirectory(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

}

/// <summary>
/// Repairs workflow artifacts by validating workflow YAML and ensuring wfvars exists/aligns with workflow inputs.
/// </summary>
[McpTool("repair_workflow_artifacts", "Repairs workflow artifacts: validates workflow and creates/validates wfvars", Category = "Generation", Level = "L1")]
public sealed class RepairWorkflowArtifactsTool : IMcpTool
{
    private readonly SihServicesAdapter _adapter;
    private readonly WorkflowValidatorService _workflowValidator;
    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public RepairWorkflowArtifactsTool(SihServicesAdapter adapter)
    {
        _adapter = adapter;
        _workflowValidator = new WorkflowValidatorService(adapter);
    }

    public string Name => "repair_workflow_artifacts";
    public string Description => "Validates workflow and ensures wfvars exists and matches workflow inputs.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            workflowPath = new { type = "string", description = "Workflow file path (.workflow/.yaml), absolute or relative to workflows root" },
            wfvarsPath = new { type = "string", description = "Optional wfvars path override; defaults to workflow sibling with .wfvars extension" },
            writeChanges = new { type = "boolean", description = "Write repairs to disk (default: true)" }
        },
        required = new[] { "workflowPath" }
    };

    public async Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var workflowPathArg = arguments?.GetValueOrDefault("workflowPath")?.ToString()
            ?? throw new ArgumentException("workflowPath is required");
        var wfvarsPathArg = arguments?.GetValueOrDefault("wfvarsPath")?.ToString();
        var writeChanges = ToolArgumentParser.TryReadBool(arguments, "writeChanges", true);

        var workflowPath = ResolvePath(workflowPathArg);
        if (!File.Exists(workflowPath))
        {
            throw new FileNotFoundException($"Workflow file not found: {workflowPath}", workflowPath);
        }

        var workflowYaml = await File.ReadAllTextAsync(workflowPath);
        var workflowValidation = await _workflowValidator.ValidateWorkflowAsync(workflowPath);
        var inputDefinitions = ExtractWorkflowInputs(workflowYaml);
        var inputNames = inputDefinitions.Select(x => x.Name).ToList();
        var requiredInputNames = inputDefinitions.Where(x => x.Required).Select(x => x.Name).ToList();

        var resolvedWfvarsPath = ResolveWfvarsPath(workflowPath, wfvarsPathArg);
        var wfvarsExists = File.Exists(resolvedWfvarsPath);
        var wfvarsCreated = false;
        var wfvarsUpdated = false;
        var repairs = new List<string>();
        var warnings = new List<string>();
        string? wfvarsYaml;

        if (inputNames.Count == 0)
        {
            wfvarsYaml = wfvarsExists ? await File.ReadAllTextAsync(resolvedWfvarsPath) : null;
            var noInputParseErrors = wfvarsExists ? ParseWfvars(wfvarsYaml!).errors : [];
            if (wfvarsExists)
            {
                warnings.Add("Workflow has no inputs; existing wfvars is optional.");
            }

            return new
            {
                workflowPath,
                wfvarsPath = resolvedWfvarsPath,
                writeChanges,
                workflowValidation = ToValidationContract(workflowValidation),
                wfvars = new
                {
                    exists = wfvarsExists,
                    created = false,
                    updated = false,
                    isValid = !wfvarsExists || noInputParseErrors.Count == 0,
                    missingRequiredInputs = Array.Empty<string>(),
                    missingOptionalInputs = Array.Empty<string>(),
                    extraKeys = Array.Empty<string>(),
                    parseErrors = noInputParseErrors,
                    draft = (string?)null
                },
                repairs,
                warnings
            };
        }

        if (!wfvarsExists)
        {
            wfvarsYaml = WorkflowArtifactHelper.GenerateWfvars(workflowYaml)
                ?? throw new InvalidOperationException("Failed to generate wfvars draft from workflow inputs.");
            wfvarsCreated = true;
            repairs.Add($"wfvars file was missing and generated at '{resolvedWfvarsPath}'.");

            if (writeChanges)
            {
                EnsureDirectory(resolvedWfvarsPath);
                await File.WriteAllTextAsync(resolvedWfvarsPath, wfvarsYaml);
            }
        }
        else
        {
            wfvarsYaml = await File.ReadAllTextAsync(resolvedWfvarsPath);
        }

        var (wfvarsData, wfvarsParseErrors) = ParseWfvars(wfvarsYaml);
        var missingRequiredInputs = requiredInputNames.Where(name => !wfvarsData.ContainsKey(name)).ToList();
        var missingOptionalInputs = inputNames.Except(requiredInputNames, StringComparer.OrdinalIgnoreCase)
            .Where(name => !wfvarsData.ContainsKey(name))
            .ToList();
        var extraKeys = wfvarsData.Keys.Where(key => !inputNames.Contains(key, StringComparer.OrdinalIgnoreCase)).ToList();

        if (wfvarsParseErrors.Count > 0)
        {
            warnings.Add("wfvars file could not be fully parsed as key/value YAML map.");
        }

        if (missingRequiredInputs.Count > 0 || missingOptionalInputs.Count > 0)
        {
            var generatedMap = ParseWfvars(WorkflowArtifactHelper.GenerateWfvars(workflowYaml) ?? string.Empty).values;
            foreach (var missing in missingRequiredInputs.Concat(missingOptionalInputs))
            {
                if (!wfvarsData.ContainsKey(missing) && generatedMap.TryGetValue(missing, out var generatedValue))
                {
                    wfvarsData[missing] = generatedValue;
                }
            }

            wfvarsUpdated = true;
            repairs.Add("wfvars file was repaired by adding missing input keys.");
        }

        if ((wfvarsCreated || wfvarsUpdated) && writeChanges)
        {
            var serializer = new SerializerBuilder().Build();
            await File.WriteAllTextAsync(resolvedWfvarsPath, serializer.Serialize(wfvarsData));
            wfvarsYaml = await File.ReadAllTextAsync(resolvedWfvarsPath);
        }

        var wfvarsIsValid = wfvarsParseErrors.Count == 0 && missingRequiredInputs.Count == 0;
        return new
        {
            workflowPath,
            wfvarsPath = resolvedWfvarsPath,
            writeChanges,
            workflowValidation = ToValidationContract(workflowValidation),
            wfvars = new
            {
                exists = File.Exists(resolvedWfvarsPath),
                created = wfvarsCreated,
                updated = wfvarsUpdated,
                isValid = wfvarsIsValid,
                missingRequiredInputs,
                missingOptionalInputs,
                extraKeys,
                parseErrors = wfvarsParseErrors,
                draft = !writeChanges ? wfvarsYaml : null
            },
            repairs,
            warnings
        };
    }

    private static object ToValidationContract(ValidationResult result)
    {
        return new
        {
            isValid = result.Valid,
            errors = result.Errors.Select(e => new
            {
                category = e.Category,
                stage = e.Stage,
                field = e.Field,
                message = e.Message,
                suggestion = e.Suggestion,
                location = e.Location
            }).ToList(),
            warnings = result.Warnings.Select(w => new
            {
                category = w.Category,
                message = w.Message,
                suggestion = w.Suggestion
            }).ToList()
        };
    }

    private static List<(string Name, bool Required)> ExtractWorkflowInputs(string workflowYaml)
    {
        if (string.IsNullOrWhiteSpace(workflowYaml))
        {
            return [];
        }

        var workflow = YamlDeserializer.Deserialize<WorkflowDefinition>(workflowYaml);
        if (workflow?.Input == null)
        {
            return [];
        }

        return workflow.Input
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .Select(x => (x.Name, x.Required))
            .DistinctBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static (Dictionary<string, string> values, List<string> errors) ParseWfvars(string wfvarsYaml)
    {
        var errors = new List<string>();
        try
        {
            var raw = YamlDeserializer.Deserialize<Dictionary<string, object?>>(wfvarsYaml) ?? [];
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in raw)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                map[key] = value?.ToString() ?? string.Empty;
            }

            return (map, errors);
        }
        catch (Exception ex)
        {
            errors.Add($"Failed to parse wfvars YAML: {ex.Message}");
            return (new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), errors);
        }
    }

    private string ResolvePath(string path)
    {
        return WorkflowPathResolver.ResolveExistingWorkflowPath(_adapter, path);
    }

    private static string ResolveWfvarsPath(string workflowPath, string? wfvarsPathArg)
    {
        if (!string.IsNullOrWhiteSpace(wfvarsPathArg))
        {
            if (Path.IsPathRooted(wfvarsPathArg))
            {
                return Path.GetFullPath(wfvarsPathArg);
            }

            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(workflowPath) ?? string.Empty, wfvarsPathArg));
        }

        return Path.ChangeExtension(workflowPath, ".wfvars");
    }

    private static void EnsureDirectory(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

}

/// <summary>
/// Generates startup bootstrap artifacts so an API can execute a workflow on application start.
/// </summary>
[McpTool("generate_startup_bootstrap", "Generates startup integration snippets and command to run workflow on app start", Category = "Generation", Level = "L1")]
public sealed class GenerateStartupBootstrapTool : IMcpTool
{
    public string Name => "generate_startup_bootstrap";
    public string Description => "Produces startup command + .NET snippets to invoke SphereIntegrationHub CLI during application startup.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            workflowPath = new { type = "string", description = "Workflow path to execute at startup" },
            environment = new { type = "string", description = "Target environment (pre/prod/local/etc.)" },
            varsFilePath = new { type = "string", description = "Optional .wfvars path" },
            catalogPath = new { type = "string", description = "Optional catalog path override" },
            envFilePath = new { type = "string", description = "Optional .env file override" },
            refreshCache = new { type = "boolean", description = "Enable --refresh-cache" },
            mocked = new { type = "boolean", description = "Enable --mocked" },
            dryRun = new { type = "boolean", description = "Enable --dry-run (bootstrap validation mode)" }
        },
        required = new[] { "workflowPath", "environment" }
    };

    public Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var workflowPath = arguments?.GetValueOrDefault("workflowPath")?.ToString()
            ?? throw new ArgumentException("workflowPath is required");
        var environment = arguments?.GetValueOrDefault("environment")?.ToString()
            ?? throw new ArgumentException("environment is required");
        var varsFilePath = arguments?.GetValueOrDefault("varsFilePath")?.ToString();
        var catalogPath = arguments?.GetValueOrDefault("catalogPath")?.ToString();
        var envFilePath = arguments?.GetValueOrDefault("envFilePath")?.ToString();
        var refreshCache = ToolArgumentParser.TryReadBool(arguments, "refreshCache");
        var mocked = ToolArgumentParser.TryReadBool(arguments, "mocked");
        var dryRun = ToolArgumentParser.TryReadBool(arguments, "dryRun");

        var cliArgs = BuildCliArgs(
            workflowPath,
            environment,
            varsFilePath,
            catalogPath,
            envFilePath,
            refreshCache,
            mocked,
            dryRun);

        var startupCommand = $"SphereIntegrationHub.cli {cliArgs}";

        var appSettingsSection = $$"""
"WorkflowBootstrap": {
  "Enabled": true,
  "Command": "SphereIntegrationHub.cli",
  "Arguments": "{{cliArgs.Replace("\"", "\\\"")}}"
}
""";

        var hostedServiceClass = $$"""
using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

public sealed class WorkflowBootstrapHostedService : IHostedService
{
    private readonly ILogger<WorkflowBootstrapHostedService> _logger;
    private readonly IConfiguration _configuration;

    public WorkflowBootstrapHostedService(
        ILogger<WorkflowBootstrapHostedService> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var enabled = _configuration.GetValue<bool>("WorkflowBootstrap:Enabled");
        if (!enabled)
        {
            return;
        }

        var command = _configuration["WorkflowBootstrap:Command"] ?? "SphereIntegrationHub.cli";
        var arguments = _configuration["WorkflowBootstrap:Arguments"] ?? "{{cliArgs.Replace("\"", "\\\"")}}";

        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start workflow bootstrap process.");

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            _logger.LogError("Workflow bootstrap failed ({ExitCode}). stderr: {Stderr}", process.ExitCode, stderr);
            throw new InvalidOperationException($"Workflow bootstrap failed: {stderr}");
        }

        _logger.LogInformation("Workflow bootstrap completed. stdout: {Stdout}", stdout);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
""";

        var programSnippet = """
builder.Services.AddHostedService<WorkflowBootstrapHostedService>();
""";

        return Task.FromResult<object>(new
        {
            startupCommand,
            appSettingsSection,
            hostedServiceClass,
            programRegistrationSnippet = programSnippet,
            notes = new[]
            {
                "Set WorkflowBootstrap:Enabled=false for environments where bootstrap should not run.",
                "Use --dry-run in early adoption to validate workflow at startup without HTTP calls."
            }
        });
    }

    private static string BuildCliArgs(
        string workflowPath,
        string environment,
        string? varsFilePath,
        string? catalogPath,
        string? envFilePath,
        bool refreshCache,
        bool mocked,
        bool dryRun)
    {
        var parts = new List<string>
        {
            "--workflow",
            Quote(workflowPath),
            "--env",
            Quote(environment)
        };

        if (!string.IsNullOrWhiteSpace(varsFilePath))
        {
            parts.Add("--varsfile");
            parts.Add(Quote(varsFilePath));
        }

        if (!string.IsNullOrWhiteSpace(catalogPath))
        {
            parts.Add("--catalog");
            parts.Add(Quote(catalogPath));
        }

        if (!string.IsNullOrWhiteSpace(envFilePath))
        {
            parts.Add("--envfile");
            parts.Add(Quote(envFilePath));
        }

        if (refreshCache)
        {
            parts.Add("--refresh-cache");
        }

        if (dryRun)
        {
            parts.Add("--dry-run");
        }
        else if (mocked)
        {
            parts.Add("--mocked");
        }

        return string.Join(" ", parts);
    }

    private static string Quote(string value)
    {
        return $"\"{value.Replace("\"", "\\\"")}\"";
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
[McpTool("refresh_swagger_cache_from_catalog", "Downloads swagger files from api-catalog definitions into cache", Category = "Generation", Level = "L1")]
public sealed class RefreshSwaggerCacheFromCatalogTool : IMcpTool
{
    private const string DefaultEnvironment = "pre";
    private readonly SihServicesAdapter _adapter;

    public RefreshSwaggerCacheFromCatalogTool(SihServicesAdapter adapter)
    {
        _adapter = adapter;
    }

    public string Name => "refresh_swagger_cache_from_catalog";
    public string Description => "Refreshes swagger cache for a catalog version (all definitions or selected apiNames).";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            version = new { type = "string", description = "Catalog version to process" },
            environment = new { type = "string", description = "Environment key for relative swaggerUrl (default: pre)" },
            refresh = new { type = "boolean", description = "Force redownload even if cache exists (default: false)" },
            apiNames = new
            {
                type = "array",
                description = "Optional subset of definition names",
                items = new { type = "string" }
            }
        },
        required = new[] { "version" }
    };

    public async Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var version = arguments?.GetValueOrDefault("version")?.ToString()
            ?? throw new ArgumentException("version is required");
        var environment = arguments?.GetValueOrDefault("environment")?.ToString() ?? DefaultEnvironment;
        var refresh = ToolArgumentParser.TryReadBool(arguments, "refresh", false);
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

/// <summary>
/// Fast-path cache refresh to reduce exploratory/tool-chaining behavior in LLM agents.
/// </summary>
[McpTool("quick_refresh_swagger_cache", "Fast path: refreshes swagger cache from api-catalog with practical defaults (version=0.1, environment=local, refresh=true)", Category = "Generation", Level = "L1")]
public sealed class QuickRefreshSwaggerCacheTool : IMcpTool
{
    private readonly RefreshSwaggerCacheFromCatalogTool _refreshTool;

    public QuickRefreshSwaggerCacheTool(SihServicesAdapter adapter)
    {
        _refreshTool = new RefreshSwaggerCacheFromCatalogTool(adapter);
    }

    public string Name => "quick_refresh_swagger_cache";
    public string Description =>
        "Use this first when user asks to regenerate cache. Executes refresh_swagger_cache_from_catalog with defaults to avoid extra discovery.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            version = new { type = "string", description = "Catalog version (default: 0.1)" },
            environment = new { type = "string", description = "Environment key (default: local)" },
            refresh = new { type = "boolean", description = "Force redownload (default: true)" },
            apiNames = new
            {
                type = "array",
                description = "Optional subset of definition names",
                items = new { type = "string" }
            }
        }
    };

    public Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var delegatedArgs = new Dictionary<string, object>
        {
            ["version"] = arguments?.GetValueOrDefault("version")?.ToString() ?? "0.1",
            ["environment"] = arguments?.GetValueOrDefault("environment")?.ToString() ?? "local",
            ["refresh"] = ToolArgumentParser.TryReadBool(arguments, "refresh", true)
        };

        if (arguments?.TryGetValue("apiNames", out var apiNamesObj) == true)
        {
            delegatedArgs["apiNames"] = apiNamesObj;
        }

        return _refreshTool.ExecuteAsync(delegatedArgs);
    }

}
