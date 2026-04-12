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

[McpTool("generate_endpoint_stage", "Generates a workflow stage definition from an API endpoint", Category = "Generation", Level = "L1")]
public sealed class GenerateEndpointStageTool : IMcpTool
{
    private readonly IStageGenerator _generator;

    public GenerateEndpointStageTool(SihServicesAdapter adapter)
        : this(adapter, new StageGenerator(adapter)) { }

    internal GenerateEndpointStageTool(SihServicesAdapter adapter, IStageGenerator generator)
    {
        _ = adapter;
        _generator = generator;
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

        return new GenerateStageResult(
            StageName: stageName ?? "generated-stage",
            Yaml: yaml,
            Source: fallbackEndpoint == null ? "swagger-cache" : "endpoint-schema-fallback");
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
        : this(adapter, new StageGenerator(adapter)) { }

    internal GenerateWorkflowSkeletonTool(SihServicesAdapter adapter, IStageGenerator generator)
    {
        _generator = generator;
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
                description = "Input parameter names. Use objectInputParameters/arrayInputParameters for complex JSON inputs.",
                items = new { type = "string" }
            },
            objectInputParameters = new
            {
                type = "array",
                description = "Input parameter names that should be emitted with type Object",
                items = new { type = "string" }
            },
            arrayInputParameters = new
            {
                type = "array",
                description = "Input parameter names that should be emitted with type Array",
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
        List<string> objectInputParameters = [];
        List<string> arrayInputParameters = [];
        if (arguments?.TryGetValue("inputParameters", out var inputParamsObj) == true)
        {
            inputParameters = ReadStringList(inputParamsObj);
        }

        if (arguments?.TryGetValue("objectInputParameters", out var objectParamsObj) == true)
        {
            objectInputParameters = ReadStringList(objectParamsObj);
        }

        if (arguments?.TryGetValue("arrayInputParameters", out var arrayParamsObj) == true)
        {
            arrayInputParameters = ReadStringList(arrayParamsObj);
        }

        var yaml = _generator.GenerateWorkflowSkeleton(name, description, inputParameters, objectInputParameters, arrayInputParameters, version);
        var wfvars = WorkflowArtifactHelper.GenerateWfvars(yaml);
        return new WorkflowSkeletonResult(
            Name: name,
            Version: version,
            Yaml: yaml,
            Wfvars: wfvars,
            AuthoringHints:
            [
                "Prefer ensure for create-if-missing or bootstrap HTTP stages.",
                "Use expectedStatuses plus onStatus/jumpOnStatus when branching needs explicit status control.",
                "Use bodyFile for large request payloads and dataFile + forEach for seed collections.",
                "Use Object/Array input types when the workflow consumes structured JSON."
            ],
            Warnings: warningMessages);
    }

    private static List<string> ReadStringList(object source)
    {
        if (source is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<string>>(jsonElement.GetRawText()) ?? [];
        }

        if (source is List<object> list)
        {
            return list
                .Select(item => item.ToString() ?? string.Empty)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToList();
        }

        return [];
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
        : this(adapter, new StageGenerator(adapter)) { }

    internal GenerateMockPayloadTool(SihServicesAdapter adapter, IStageGenerator generator)
    {
        _ = adapter;
        _generator = generator;
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
        : this(adapter, new StageGenerator(adapter)) { }

    internal GenerateWorkflowBundleTool(SihServicesAdapter adapter, IStageGenerator generator)
    {
        _generator = generator;
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
            ? Path.ChangeExtension(resolvedWorkflowPath, WorkflowConstants.ExtWfvars)
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
            extension.Equals(WorkflowConstants.ExtYaml, StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(WorkflowConstants.ExtYml, StringComparison.OrdinalIgnoreCase))
        {
            return Path.ChangeExtension(path, WorkflowConstants.ExtWorkflow);
        }

        return path;
    }

    private static string NormalizeWfvarsFileExtension(string path)
    {
        var extension = Path.GetExtension(path);
        if (!extension.Equals(WorkflowConstants.ExtWfvars, StringComparison.OrdinalIgnoreCase))
        {
            return Path.ChangeExtension(path, WorkflowConstants.ExtWfvars);
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

    private static object ToValidationContract(ValidationResult result) =>
        ValidationResultMapper.ToContract(result);

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

        return Path.ChangeExtension(workflowPath, WorkflowConstants.ExtWfvars);
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
