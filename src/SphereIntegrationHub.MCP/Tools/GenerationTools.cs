using System.Text.RegularExpressions;
using SphereIntegrationHub.MCP.Core;
using SphereIntegrationHub.MCP.Models;
using SphereIntegrationHub.MCP.Services.Generation;
using SphereIntegrationHub.MCP.Services.Integration;
using System.Text.Json;

namespace SphereIntegrationHub.MCP.Tools;

/// <summary>
/// Generates a workflow stage from an API endpoint
/// </summary>
[McpTool("generate_endpoint_stage", "Generates a workflow stage definition from an API endpoint", Category = "Generation", Level = "L1")]
public sealed class GenerateEndpointStageTool : IMcpTool
{
    private readonly StageGenerator _generator;

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
        var fallbackEndpoint = TryParseEndpointSchema(arguments, false);

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

    internal static EndpointInfo? TryParseEndpointSchema(Dictionary<string, object>? arguments, bool required)
    {
        if (arguments?.TryGetValue("endpointSchema", out var endpointSchemaObj) != true)
        {
            if (required)
            {
                throw new ArgumentException("endpointSchema is required");
            }

            return null;
        }

        JsonElement endpointJson;
        if (endpointSchemaObj is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            endpointJson = element;
        }
        else
        {
            throw new ArgumentException("endpointSchema must be a JSON object");
        }

        var apiName = endpointJson.TryGetProperty("apiName", out var apiEl)
            ? apiEl.GetString()
            : null;
        var endpoint = endpointJson.TryGetProperty("endpoint", out var endpointEl)
            ? endpointEl.GetString()
            : null;
        var httpVerb = endpointJson.TryGetProperty("httpVerb", out var verbEl)
            ? verbEl.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(apiName) ||
            string.IsNullOrWhiteSpace(endpoint) ||
            string.IsNullOrWhiteSpace(httpVerb))
        {
            throw new ArgumentException("endpointSchema must include apiName, endpoint and httpVerb");
        }

        var queryParameters = ParseParameters(endpointJson, "queryParameters");
        var headerParameters = ParseParameters(endpointJson, "headerParameters");
        var pathParameters = ParseParameters(endpointJson, "pathParameters");
        var bodySchema = ParseBodySchema(endpointJson);
        var responses = ParseResponses(endpointJson);

        return new EndpointInfo
        {
            ApiName = apiName!,
            Endpoint = endpoint!,
            HttpVerb = httpVerb!.ToUpperInvariant(),
            Summary = endpointJson.TryGetProperty("summary", out var summaryEl) ? summaryEl.GetString() ?? string.Empty : string.Empty,
            Description = endpointJson.TryGetProperty("description", out var descriptionEl) ? descriptionEl.GetString() ?? string.Empty : string.Empty,
            QueryParameters = queryParameters,
            HeaderParameters = headerParameters,
            PathParameters = pathParameters,
            BodySchema = bodySchema,
            Responses = responses,
            Tags = endpointJson.TryGetProperty("tags", out var tagsEl) && tagsEl.ValueKind == JsonValueKind.Array
                ? tagsEl.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(x => !string.IsNullOrWhiteSpace(x)).ToList()
                : []
        };
    }

    private static List<ParameterInfo> ParseParameters(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var arrayEl) || arrayEl.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var parameters = new List<ParameterInfo>();
        foreach (var item in arrayEl.EnumerateArray())
        {
            var name = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var type = item.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : "string";
            var required = item.TryGetProperty("required", out var reqEl) && reqEl.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? reqEl.GetBoolean()
                : false;

            parameters.Add(new ParameterInfo
            {
                Name = name!,
                Type = string.IsNullOrWhiteSpace(type) ? "string" : type!,
                Required = required,
                Description = item.TryGetProperty("description", out var descEl) ? descEl.GetString() : null,
                DefaultValue = item.TryGetProperty("defaultValue", out var defaultEl) ? defaultEl.ToString() : null
            });
        }

        return parameters;
    }

    private static BodySchema? ParseBodySchema(JsonElement root)
    {
        if (!root.TryGetProperty("bodySchema", out var bodyEl) || bodyEl.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!bodyEl.TryGetProperty("fields", out var fieldsEl) || fieldsEl.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var fields = new Dictionary<string, FieldSchema>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in fieldsEl.EnumerateObject())
        {
            var fieldValue = property.Value;
            var type = fieldValue.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : "string";

            fields[property.Name] = new FieldSchema
            {
                Type = string.IsNullOrWhiteSpace(type) ? "string" : type!,
                Format = fieldValue.TryGetProperty("format", out var formatEl) ? formatEl.GetString() : null,
                Description = fieldValue.TryGetProperty("description", out var descriptionEl) ? descriptionEl.GetString() : null,
                IsArray = fieldValue.TryGetProperty("isArray", out var isArrayEl) &&
                    isArrayEl.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                    isArrayEl.GetBoolean(),
                EnumValues = fieldValue.TryGetProperty("enumValues", out var enumEl) && enumEl.ValueKind == JsonValueKind.Array
                    ? enumEl.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(x => !string.IsNullOrWhiteSpace(x)).ToList()
                    : null
            };
        }

        var requiredFields = bodyEl.TryGetProperty("requiredFields", out var requiredEl) && requiredEl.ValueKind == JsonValueKind.Array
            ? requiredEl.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(x => !string.IsNullOrWhiteSpace(x)).ToList()
            : [];

        return new BodySchema
        {
            Fields = fields,
            RequiredFields = requiredFields
        };
    }

    private static Dictionary<int, ResponseSchema> ParseResponses(JsonElement root)
    {
        if (!root.TryGetProperty("responses", out var responsesEl) || responsesEl.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var responses = new Dictionary<int, ResponseSchema>();
        foreach (var responseEl in responsesEl.EnumerateArray())
        {
            if (!responseEl.TryGetProperty("statusCode", out var statusCodeEl) || !statusCodeEl.TryGetInt32(out var statusCode))
            {
                continue;
            }

            responses[statusCode] = new ResponseSchema
            {
                StatusCode = statusCode,
                Description = responseEl.TryGetProperty("description", out var descEl) ? descEl.GetString() ?? string.Empty : string.Empty,
                Fields = null
            };
        }

        return responses;
    }
}

/// <summary>
/// Generates a workflow skeleton
/// </summary>
[McpTool("generate_workflow_skeleton", "Generates a complete workflow skeleton with basic structure", Category = "Generation", Level = "L1")]
public sealed class GenerateWorkflowSkeletonTool : IMcpTool
{
    private readonly StageGenerator _generator;

    public GenerateWorkflowSkeletonTool(SihServicesAdapter adapter)
    {
        _generator = new StageGenerator(adapter);
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
            version = new { type = "string", description = "Catalog version (default: 3.11)" },
            inputParameters = new
            {
                type = "array",
                description = "Input parameter names",
                items = new { type = "string" }
            }
        },
        required = new[] { "name", "description" }
    };

    public Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var name = arguments?.GetValueOrDefault("name")?.ToString()
            ?? throw new ArgumentException("name is required");
        var description = arguments?.GetValueOrDefault("description")?.ToString()
            ?? throw new ArgumentException("description is required");
        var version = arguments?.GetValueOrDefault("version")?.ToString() ?? "3.11";

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
        return Task.FromResult<object>(new { name, yaml });
    }
}

/// <summary>
/// Generates a mock payload for an endpoint
/// </summary>
[McpTool("generate_mock_payload", "Generates a mock JSON payload for an API endpoint", Category = "Generation", Level = "L1")]
public sealed class GenerateMockPayloadTool : IMcpTool
{
    private readonly StageGenerator _generator;

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
        var fallbackEndpoint = GenerateEndpointStageTool.TryParseEndpointSchema(arguments, false);
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
    private readonly StageGenerator _generator;

    public GenerateWorkflowBundleTool(SihServicesAdapter adapter)
    {
        _generator = new StageGenerator(adapter);
    }

    public string Name => "generate_workflow_bundle";
    public string Description => "Creates a complete bundle for automation: workflow draft, wfvars draft, and payload templates.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            version = new { type = "string", description = "Catalog version" },
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
        required = new[] { "version", "workflowName", "apiName", "endpoints" }
    };

    public async Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var version = arguments?.GetValueOrDefault("version")?.ToString()
            ?? throw new ArgumentException("version is required");
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
                : GenerateEndpointStageTool.TryParseEndpointSchema(endpointSchema, true);

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

        var wfvarsDraft = _generator.GenerateWfvars(inputNames);

        return new
        {
            workflowDraft,
            wfvarsDraft,
            payloadDrafts = payloads
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

        var resolvedWorkflowPath = ResolveTargetPath(workflowPath);
        EnsureDirectory(resolvedWorkflowPath);
        File.WriteAllText(resolvedWorkflowPath, workflowYaml);
        writtenFiles.Add(resolvedWorkflowPath);

        if (!string.IsNullOrWhiteSpace(wfvarsPath) && !string.IsNullOrWhiteSpace(wfvarsYaml))
        {
            var resolvedWfvarsPath = ResolveTargetPath(wfvarsPath);
            EnsureDirectory(resolvedWfvarsPath);
            File.WriteAllText(resolvedWfvarsPath, wfvarsYaml);
            writtenFiles.Add(resolvedWfvarsPath);
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
            writtenFiles,
            count = writtenFiles.Count
        });
    }

    private string ResolveTargetPath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(Path.Combine(_adapter.WorkflowsPath, path));
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
        var refreshCache = TryReadBool(arguments, "refreshCache");
        var mocked = TryReadBool(arguments, "mocked");
        var dryRun = TryReadBool(arguments, "dryRun");

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

    private static bool TryReadBool(Dictionary<string, object>? arguments, string key)
    {
        if (arguments?.TryGetValue(key, out var obj) != true)
        {
            return false;
        }

        if (obj is bool boolValue)
        {
            return boolValue;
        }

        return obj.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
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
                                    basePath = new { type = "string" },
                                    swaggerUrl = new { type = "string" }
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

        var versions = new List<object>();
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

            var definitions = new List<object>();
            foreach (var definitionItem in defsEl.EnumerateArray())
            {
                var name = definitionItem.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                var basePath = definitionItem.TryGetProperty("basePath", out var basePathEl) ? basePathEl.GetString() : null;
                var swaggerUrl = definitionItem.TryGetProperty("swaggerUrl", out var swaggerEl) ? swaggerEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(name) ||
                    string.IsNullOrWhiteSpace(basePath) ||
                    string.IsNullOrWhiteSpace(swaggerUrl))
                {
                    throw new ArgumentException("Definition requires name, basePath, swaggerUrl");
                }

                definitions.Add(new
                {
                    name,
                    basePath,
                    swaggerUrl
                });
            }

            versions.Add(new
            {
                version,
                baseUrl,
                definitions
            });
        }

        var json = JsonSerializer.Serialize(versions, new JsonSerializerOptions { WriteIndented = true });
        var writeToDisk = TryReadBool(arguments, "writeToDisk", true);
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

    private static bool TryReadBool(Dictionary<string, object>? arguments, string key, bool defaultValue)
    {
        if (arguments?.TryGetValue(key, out var obj) != true)
        {
            return defaultValue;
        }

        if (obj is bool boolValue)
        {
            return boolValue;
        }

        return obj.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
    }
}
