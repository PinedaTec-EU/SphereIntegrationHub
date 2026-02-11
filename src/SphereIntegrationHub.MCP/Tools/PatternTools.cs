using SphereIntegrationHub.MCP.Core;
using SphereIntegrationHub.MCP.Models;
using SphereIntegrationHub.MCP.Services.Catalog;
using SphereIntegrationHub.MCP.Services.Integration;
using SphereIntegrationHub.MCP.Services.Semantic;
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

    public GenerateCrudWorkflowTool(SihServicesAdapter adapter)
    {
        _detector = new PatternDetector(adapter);
        _swaggerReader = new SwaggerReader(adapter);
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
                description = "API catalog version"
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
        required = new[] { "version", "apiName", "resource", "operations" }
    };

    public async Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var version = arguments?.GetValueOrDefault("version")?.ToString()
            ?? throw new ArgumentException("version is required");
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
        var yaml = GenerateCrudWorkflowYaml(workflowName, resource, crudPattern, operations, endpoints);

        return new
        {
            workflowName,
            resource,
            operations,
            yaml,
            crudPattern = new
            {
                resource = crudPattern.Resource,
                endpoints = crudPattern.Endpoints,
                confidence = crudPattern.Confidence
            }
        };
    }

    private static string GenerateCrudWorkflowYaml(
        string workflowName,
        string resource,
        CrudPattern pattern,
        List<string> operations,
        List<EndpointInfo> allEndpoints)
    {
        var yaml = $@"name: {workflowName}
version: 1.0
description: CRUD operations for {resource}

input:
  - name: apiKey
    type: string
    required: true
";

        // Add specific inputs based on operations
        if (operations.Contains("create") || operations.Contains("update"))
        {
            yaml += $@"  - name: {resource}Data
    type: object
    required: true
";
        }

        if (operations.Contains("read") || operations.Contains("update") || operations.Contains("delete"))
        {
            yaml += $@"  - name: {pattern.IdParameter}
    type: {pattern.IdType}
    required: true
";
        }

        yaml += "\nstages:\n";

        var stageNum = 1;

        // Generate stages for each operation
        if (operations.Contains("list"))
        {
            if (pattern.Endpoints.TryGetValue("list", out var listEndpoint))
            {
                var parts = listEndpoint.Split(' ');
                var verb = parts[0];
                var endpoint = parts.Length > 1 ? parts[1] : "";

                yaml += $@"  - name: list_{resource}
    type: api
    api: {allEndpoints.First().ApiName}
    endpoint: {endpoint}
    verb: {verb}
    output:
      save: true
      context: listResult

";
                stageNum++;
            }
        }

        if (operations.Contains("create"))
        {
            if (pattern.Endpoints.TryGetValue("create", out var createEndpoint))
            {
                var parts = createEndpoint.Split(' ');
                var verb = parts[0];
                var endpoint = parts.Length > 1 ? parts[1] : "";

                yaml += $@"  - name: create_{resource}
    type: api
    api: {allEndpoints.First().ApiName}
    endpoint: {endpoint}
    verb: {verb}
    body:
      data: ""{{{{ input.{resource}Data }}}}""
    output:
      save: true
      context: createdResult

";
                stageNum++;
            }
        }

        if (operations.Contains("read"))
        {
            if (pattern.Endpoints.TryGetValue("read", out var readEndpoint))
            {
                var parts = readEndpoint.Split(' ');
                var verb = parts[0];
                var endpoint = parts.Length > 1 ? parts[1] : "";

                yaml += $@"  - name: read_{resource}
    type: api
    api: {allEndpoints.First().ApiName}
    endpoint: {endpoint}
    verb: {verb}
    pathParams:
      {pattern.IdParameter}: ""{{{{ input.{pattern.IdParameter} }}}}""
    output:
      save: true
      context: readResult

";
                stageNum++;
            }
        }

        if (operations.Contains("update"))
        {
            if (pattern.Endpoints.TryGetValue("update", out var updateEndpoint))
            {
                var parts = updateEndpoint.Split(' ');
                var verb = parts[0];
                var endpoint = parts.Length > 1 ? parts[1] : "";

                yaml += $@"  - name: update_{resource}
    type: api
    api: {allEndpoints.First().ApiName}
    endpoint: {endpoint}
    verb: {verb}
    pathParams:
      {pattern.IdParameter}: ""{{{{ input.{pattern.IdParameter} }}}}""
    body:
      data: ""{{{{ input.{resource}Data }}}}""
    output:
      save: true
      context: updateResult

";
                stageNum++;
            }
        }

        if (operations.Contains("delete"))
        {
            if (pattern.Endpoints.TryGetValue("delete", out var deleteEndpoint))
            {
                var parts = deleteEndpoint.Split(' ');
                var verb = parts[0];
                var endpoint = parts.Length > 1 ? parts[1] : "";

                yaml += $@"  - name: delete_{resource}
    type: api
    api: {allEndpoints.First().ApiName}
    endpoint: {endpoint}
    verb: {verb}
    pathParams:
      {pattern.IdParameter}: ""{{{{ input.{pattern.IdParameter} }}}}""
    output:
      save: true
      context: deleteResult

";
                stageNum++;
            }
        }

        yaml += @"end-stage:
  output:
    success: true
    result: ""{{ stages }}""
";

        return yaml;
    }
}
