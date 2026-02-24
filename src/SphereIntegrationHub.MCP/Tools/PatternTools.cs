using SphereIntegrationHub.MCP.Core;
using SphereIntegrationHub.MCP.Models;
using SphereIntegrationHub.MCP.Services.Catalog;
using SphereIntegrationHub.MCP.Services.Generation;
using SphereIntegrationHub.MCP.Services.Integration;
using SphereIntegrationHub.MCP.Services.Semantic;
using System.Text;
using System.Text.Json;

namespace SphereIntegrationHub.MCP.Tools;

/// <summary>
/// Detects API patterns (OAuth, CRUD, Pagination, etc.)
/// </summary>
[McpTool("detect_api_patterns", "Detects common API patterns like OAuth, CRUD, Pagination in an API specification", Category = "Pattern", Level = "L2")]
public sealed class DetectApiPatternsTool : IMcpTool
{
    private readonly PatternDetector _detector;

    public DetectApiPatternsTool(SihServicesAdapter adapter)
    {
        _detector = new PatternDetector(adapter);
    }

    public string Name => "detect_api_patterns";
    public string Description => "Detects OAuth, CRUD, pagination, filtering, and batch operation patterns in an API";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            version = new
            {
                type = "string",
                description = "API catalog version"
            },
            apiName = new
            {
                type = "string",
                description = "Name of the API to analyze"
            }
        },
        required = new[] { "version", "apiName" }
    };

    public async Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var version = arguments?.GetValueOrDefault("version")?.ToString()
            ?? throw new ArgumentException("version is required");
        var apiName = arguments?.GetValueOrDefault("apiName")?.ToString()
            ?? throw new ArgumentException("apiName is required");

        var patterns = await _detector.DetectPatternsAsync(version, apiName);

        return new
        {
            apiName,
            version,
            patterns = patterns.Patterns.Select(p => SerializePattern(p)).ToList(),
            patternCount = patterns.Patterns.Count,
            summary = GeneratePatternSummary(patterns)
        };
    }

    private static object SerializePattern(ApiPattern pattern)
    {
        return pattern switch
        {
            OAuth2Pattern oauth => new
            {
                type = oauth.Type,
                confidence = oauth.Confidence,
                endpoints = oauth.Endpoints,
                grantTypes = oauth.GrantTypes,
                tokenLocation = oauth.TokenLocation
            },
            CrudPattern crud => new
            {
                type = crud.Type,
                confidence = crud.Confidence,
                resource = crud.Resource,
                endpoints = crud.Endpoints,
                idParameter = crud.IdParameter,
                idType = crud.IdType
            },
            PaginationPattern pagination => new
            {
                type = pagination.Type,
                confidence = pagination.Confidence,
                mechanism = pagination.Mechanism,
                queryParams = pagination.QueryParams,
                responseSchema = new
                {
                    dataField = pagination.ResponseSchema.DataField,
                    totalField = pagination.ResponseSchema.TotalField,
                    hasNextField = pagination.ResponseSchema.HasNextField
                }
            },
            FilteringPattern filtering => new
            {
                type = filtering.Type,
                confidence = filtering.Confidence,
                queryParams = filtering.QueryParams
            },
            BatchOperationPattern batch => new
            {
                type = batch.Type,
                confidence = batch.Confidence,
                endpoint = batch.Endpoint,
                httpVerb = batch.HttpVerb,
                arrayField = batch.ArrayField
            },
            _ => new { type = pattern.Type, confidence = pattern.Confidence }
        };
    }

    private static Dictionary<string, object> GeneratePatternSummary(ApiPatternCollection patterns)
    {
        var summary = new Dictionary<string, object>
        {
            ["totalPatterns"] = patterns.Patterns.Count,
            ["hasOAuth"] = patterns.Patterns.Any(p => p is OAuth2Pattern),
            ["crudResources"] = patterns.Patterns.OfType<CrudPattern>().Select(p => p.Resource).ToList(),
            ["supportsPagination"] = patterns.Patterns.Any(p => p is PaginationPattern),
            ["supportsFiltering"] = patterns.Patterns.Any(p => p is FilteringPattern),
            ["supportsBatchOperations"] = patterns.Patterns.Any(p => p is BatchOperationPattern)
        };

        return summary;
    }
}

/// <summary>
/// Generates a CRUD workflow for a specific resource
/// </summary>
[McpTool("generate_crud_workflow", "Generates a complete CRUD workflow for a resource", Category = "Pattern", Level = "L2")]
public sealed class GenerateCrudWorkflowTool : IMcpTool
{
    private readonly PatternDetector _detector;
    private readonly SwaggerReader _swaggerReader;
    private readonly ApiCatalogReader _catalogReader;

    public GenerateCrudWorkflowTool(SihServicesAdapter adapter)
    {
        _detector = new PatternDetector(adapter);
        _swaggerReader = new SwaggerReader(adapter);
        _catalogReader = new ApiCatalogReader(adapter);
    }

    public string Name => "generate_crud_workflow";
    public string Description => "Generates a workflow YAML for CRUD operations on a specific resource";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            version = new
            {
                type = "string",
                description = "API catalog version (optional: falls back to first catalog version)"
            },
            apiName = new
            {
                type = "string",
                description = "Name of the API"
            },
            resource = new
            {
                type = "string",
                description = "Resource name (e.g., 'customers', 'orders')"
            },
            operations = new
            {
                type = "array",
                description = "Operations to include (create, read, update, delete, list)",
                items = new { type = "string" }
            }
        },
        required = new[] { "apiName", "resource", "operations" }
    };

    public async Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var warningMessages = new List<string>();
        var version = await ResolveVersionAsync(arguments?.GetValueOrDefault("version")?.ToString(), warningMessages);
        var apiName = arguments?.GetValueOrDefault("apiName")?.ToString()
            ?? throw new ArgumentException("apiName is required");
        var resource = arguments?.GetValueOrDefault("resource")?.ToString()
            ?? throw new ArgumentException("resource is required");

        List<string> operations;
        if (arguments?.TryGetValue("operations", out var opsObj) == true)
        {
            if (opsObj is JsonElement jsonElement)
            {
                operations = JsonSerializer.Deserialize<List<string>>(jsonElement.GetRawText()) ?? [];
            }
            else if (opsObj is List<object> list)
            {
                operations = list.Select(x => x.ToString() ?? "").Where(x => !string.IsNullOrEmpty(x)).ToList();
            }
            else
            {
                throw new ArgumentException("operations must be a valid array");
            }
        }
        else
        {
            throw new ArgumentException("operations is required");
        }

        // Detect CRUD patterns
        var patterns = await _detector.DetectPatternsAsync(version, apiName);
        var crudPattern = patterns.Patterns
            .OfType<CrudPattern>()
            .FirstOrDefault(p => p.Resource.Equals(resource, StringComparison.OrdinalIgnoreCase));

        if (crudPattern == null)
        {
            throw new InvalidOperationException($"CRUD pattern not found for resource: {resource}");
        }

        // Get all endpoints for this API
        var endpoints = await _swaggerReader.GetEndpointsAsync(version, apiName);

        // Build workflow
        var workflowName = $"{resource}_crud_workflow";
        var yaml = GenerateCrudWorkflowYaml(version, workflowName, resource, crudPattern, operations, endpoints);
        var wfvars = WorkflowArtifactHelper.GenerateWfvars(yaml);

        return new
        {
            version,
            workflowName,
            resource,
            operations,
            yaml,
            wfvars,
            warnings = warningMessages,
            crudPattern = new
            {
                resource = crudPattern.Resource,
                endpoints = crudPattern.Endpoints,
                confidence = crudPattern.Confidence
            }
        };
    }

    private static string GenerateCrudWorkflowYaml(
        string version,
        string workflowName,
        string resource,
        CrudPattern pattern,
        List<string> operations,
        List<EndpointInfo> allEndpoints)
    {
        var apiRef = allEndpoints.First().ApiName;
        var normalizedOperations = operations
            .Select(x => x.Trim().ToLowerInvariant())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
        var operationEndpoints = ResolveOperationEndpoints(pattern, normalizedOperations, allEndpoints);
        var inputVariables = BuildInputVariables(pattern.IdParameter, normalizedOperations, operationEndpoints);

        var builder = new StringBuilder();
        builder.AppendLine($"version: {version}");
        builder.AppendLine($"id: {Guid.NewGuid():N}");
        builder.AppendLine($"name: {workflowName}");
        builder.AppendLine($"description: CRUD operations for {resource}");
        builder.AppendLine("output: true");
        builder.AppendLine("references:");
        builder.AppendLine("  apis:");
        builder.AppendLine($"    - name: \"{apiRef}\"");
        builder.AppendLine($"      definition: \"{apiRef}\"");
        builder.AppendLine();
        AppendInputSection(builder, inputVariables);
        builder.AppendLine("stages:");

        if (normalizedOperations.Contains("list") &&
            pattern.Endpoints.TryGetValue("list", out var listEndpoint) &&
            TryParsePatternEndpoint(listEndpoint, out var listVerb, out var listPath))
        {
            AppendStage(
                builder,
                stageName: $"list_{resource}",
                apiRef,
                endpoint: listPath,
                httpVerb: listVerb,
                expectedStatus: 200,
                headers: operationEndpoints.GetValueOrDefault("list")?.HeaderParameters ?? [],
                includeJsonContentType: false,
                bodyTemplate: null);
        }

        if (normalizedOperations.Contains("create") &&
            pattern.Endpoints.TryGetValue("create", out var createEndpoint) &&
            TryParsePatternEndpoint(createEndpoint, out var createVerb, out var createPath))
        {
            AppendStage(
                builder,
                stageName: $"create_{resource}",
                apiRef,
                endpoint: createPath,
                httpVerb: createVerb,
                expectedStatus: 201,
                headers: operationEndpoints.GetValueOrDefault("create")?.HeaderParameters ?? [],
                includeJsonContentType: true,
                bodyTemplate: ResolveBodyTemplate(operationEndpoints.GetValueOrDefault("create")));
        }

        if (normalizedOperations.Contains("read") &&
            pattern.Endpoints.TryGetValue("read", out var readEndpoint) &&
            TryParsePatternEndpoint(readEndpoint, out var readVerb, out var readPath))
        {
            var resolvedPath = ApplyPathInputTemplate(readPath, pattern.IdParameter);
            AppendStage(
                builder,
                stageName: $"read_{resource}",
                apiRef,
                endpoint: resolvedPath,
                httpVerb: readVerb,
                expectedStatus: 200,
                headers: operationEndpoints.GetValueOrDefault("read")?.HeaderParameters ?? [],
                includeJsonContentType: false,
                bodyTemplate: null);
        }

        if (normalizedOperations.Contains("update") &&
            pattern.Endpoints.TryGetValue("update", out var updateEndpoint) &&
            TryParsePatternEndpoint(updateEndpoint, out var updateVerb, out var updatePath))
        {
            var resolvedPath = ApplyPathInputTemplate(updatePath, pattern.IdParameter);
            AppendStage(
                builder,
                stageName: $"update_{resource}",
                apiRef,
                endpoint: resolvedPath,
                httpVerb: updateVerb,
                expectedStatus: 200,
                headers: operationEndpoints.GetValueOrDefault("update")?.HeaderParameters ?? [],
                includeJsonContentType: true,
                bodyTemplate: ResolveBodyTemplate(operationEndpoints.GetValueOrDefault("update")));
        }

        if (normalizedOperations.Contains("delete") &&
            pattern.Endpoints.TryGetValue("delete", out var deleteEndpoint) &&
            TryParsePatternEndpoint(deleteEndpoint, out var deleteVerb, out var deletePath))
        {
            var resolvedPath = ApplyPathInputTemplate(deletePath, pattern.IdParameter);
            AppendStage(
                builder,
                stageName: $"delete_{resource}",
                apiRef,
                endpoint: resolvedPath,
                httpVerb: deleteVerb,
                expectedStatus: 200,
                headers: operationEndpoints.GetValueOrDefault("delete")?.HeaderParameters ?? [],
                includeJsonContentType: false,
                bodyTemplate: null);
        }

        var resultStage = normalizedOperations.Contains("delete")
            ? $"delete_{resource}"
            : normalizedOperations.Contains("update")
                ? $"update_{resource}"
                : normalizedOperations.Contains("read")
                    ? $"read_{resource}"
                    : normalizedOperations.Contains("create")
                        ? $"create_{resource}"
                        : $"list_{resource}";
        builder.AppendLine("endStage:");
        builder.AppendLine("  output:");
        builder.AppendLine("    success: \"true\"");
        builder.AppendLine($"    result: \"{{{{stage:{resultStage}.output.dto}}}}\"");
        return builder.ToString();
    }

    private async Task<string> ResolveVersionAsync(string? requestedVersion, List<string> warningMessages)
    {
        if (!string.IsNullOrWhiteSpace(requestedVersion))
        {
            return requestedVersion;
        }

        var versions = await _catalogReader.GetVersionsAsync();
        var fallbackVersion = versions.FirstOrDefault()
            ?? throw new InvalidOperationException("version was not provided and no catalog versions are available");

        warningMessages.Add($"version was not provided; using first catalog version '{fallbackVersion}'.");
        return fallbackVersion;
    }

    private static Dictionary<string, EndpointInfo> ResolveOperationEndpoints(
        CrudPattern pattern,
        IReadOnlyCollection<string> operations,
        List<EndpointInfo> allEndpoints)
    {
        var endpointsByOperation = new Dictionary<string, EndpointInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var operation in operations)
        {
            if (!pattern.Endpoints.TryGetValue(operation, out var endpointDefinition) ||
                !TryParsePatternEndpoint(endpointDefinition, out var verb, out var endpointPath))
            {
                continue;
            }

            var endpointInfo = allEndpoints.FirstOrDefault(e =>
                e.HttpVerb.Equals(verb, StringComparison.OrdinalIgnoreCase) &&
                e.Endpoint.Equals(endpointPath, StringComparison.OrdinalIgnoreCase));

            if (endpointInfo != null)
            {
                endpointsByOperation[operation] = endpointInfo;
            }
        }

        return endpointsByOperation;
    }

    private static List<InputVariable> BuildInputVariables(
        string idParameter,
        IReadOnlyCollection<string> operations,
        IReadOnlyDictionary<string, EndpointInfo> operationEndpoints)
    {
        var inputs = new List<InputVariable>();

        foreach (var endpointInfo in operationEndpoints.Values)
        {
            foreach (var header in endpointInfo.HeaderParameters.Where(p => p.Required))
            {
                inputs.Add(new InputVariable(ToTokenName(header.Name), true));
            }
        }

        if (operations.Contains("read") || operations.Contains("update") || operations.Contains("delete"))
        {
            inputs.Add(new InputVariable(idParameter, true));
        }

        return inputs
            .Where(i => !string.IsNullOrWhiteSpace(i.Name))
            .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static void AppendInputSection(StringBuilder builder, IReadOnlyCollection<InputVariable> inputVariables)
    {
        if (inputVariables.Count == 0)
        {
            builder.AppendLine("input: []");
            builder.AppendLine();
            return;
        }

        builder.AppendLine("input:");
        foreach (var input in inputVariables)
        {
            builder.AppendLine($"  - name: {input.Name}");
            builder.AppendLine("    type: Text");
            builder.AppendLine($"    required: {input.Required.ToString().ToLowerInvariant()}");
        }

        builder.AppendLine();
    }

    private static void AppendStage(
        StringBuilder builder,
        string stageName,
        string apiRef,
        string endpoint,
        string httpVerb,
        int expectedStatus,
        IReadOnlyCollection<ParameterInfo> headers,
        bool includeJsonContentType,
        string? bodyTemplate)
    {
        builder.AppendLine($"  - name: {stageName}");
        builder.AppendLine("    kind: Endpoint");
        builder.AppendLine($"    apiRef: {apiRef}");
        builder.AppendLine($"    endpoint: {endpoint}");
        builder.AppendLine($"    httpVerb: {httpVerb}");
        builder.AppendLine($"    expectedStatus: {expectedStatus}");

        var requiredHeaders = headers.Where(h => h.Required).ToList();
        if (requiredHeaders.Count > 0 || includeJsonContentType)
        {
            builder.AppendLine("    headers:");
            foreach (var header in requiredHeaders.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
            {
                builder.AppendLine($"      \"{header.Name}\": \"{{{{input.{ToTokenName(header.Name)}}}}}\"");
            }

            if (includeJsonContentType)
            {
                builder.AppendLine("      \"Content-Type\": \"application/json\"");
            }
        }

        if (!string.IsNullOrWhiteSpace(bodyTemplate))
        {
            builder.AppendLine("    body: |");
            foreach (var line in bodyTemplate.Split('\n'))
            {
                builder.AppendLine($"      {line.TrimEnd('\r')}");
            }
        }

        builder.AppendLine("    output:");
        builder.AppendLine("      dto: \"{{response.body}}\"");
        builder.AppendLine("      http_status: \"{{response.status}}\"");
        builder.AppendLine();
    }

    private static bool TryParsePatternEndpoint(string patternEndpoint, out string verb, out string endpoint)
    {
        verb = string.Empty;
        endpoint = string.Empty;

        if (string.IsNullOrWhiteSpace(patternEndpoint))
        {
            return false;
        }

        var separatorIndex = patternEndpoint.IndexOf(' ');
        if (separatorIndex <= 0 || separatorIndex >= patternEndpoint.Length - 1)
        {
            return false;
        }

        verb = patternEndpoint[..separatorIndex].Trim();
        endpoint = patternEndpoint[(separatorIndex + 1)..].Trim();
        return !string.IsNullOrWhiteSpace(verb) && !string.IsNullOrWhiteSpace(endpoint);
    }

    private static string ApplyPathInputTemplate(string endpoint, string idParameter)
    {
        return endpoint.Replace(
            $"{{{idParameter}}}",
            $"{{{{input.{idParameter}}}}}",
            StringComparison.OrdinalIgnoreCase);
    }

    private static string ToTokenName(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return input;
        }

        var normalized = System.Text.RegularExpressions.Regex.Replace(input, @"[^a-zA-Z0-9_]", "_");
        return char.ToLowerInvariant(normalized[0]) + normalized[1..];
    }

    private sealed record InputVariable(string Name, bool Required);

    private static string ResolveBodyTemplate(EndpointInfo? endpointInfo)
    {
        if (!string.IsNullOrWhiteSpace(endpointInfo?.BodySchema?.Example))
        {
            return endpointInfo.BodySchema.Example!;
        }

        return "{\n" +
               "  // put your payload here\n" +
               "}";
    }
}
