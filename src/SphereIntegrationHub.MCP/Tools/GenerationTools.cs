using SphereIntegrationHub.MCP.Core;
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
    public string Description => "Generates a workflow stage definition from an API endpoint with proper parameters and body";

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
            endpoint = new
            {
                type = "string",
                description = "Endpoint path"
            },
            httpVerb = new
            {
                type = "string",
                description = "HTTP verb (GET, POST, PUT, DELETE, PATCH)"
            },
            stageName = new
            {
                type = "string",
                description = "Optional custom stage name"
            }
        },
        required = new[] { "version", "apiName", "endpoint", "httpVerb" }
    };

    public async Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var version = arguments?.GetValueOrDefault("version")?.ToString()
            ?? throw new ArgumentException("version is required");
        var apiName = arguments?.GetValueOrDefault("apiName")?.ToString()
            ?? throw new ArgumentException("apiName is required");
        var endpoint = arguments?.GetValueOrDefault("endpoint")?.ToString()
            ?? throw new ArgumentException("endpoint is required");
        var httpVerb = arguments?.GetValueOrDefault("httpVerb")?.ToString()
            ?? throw new ArgumentException("httpVerb is required");
        var stageName = arguments?.GetValueOrDefault("stageName")?.ToString();

        var yaml = await _generator.GenerateEndpointStageAsync(version, apiName, endpoint, httpVerb, stageName);
        return new
        {
            stageName = stageName ?? "generated",
            yaml
        };
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
    public string Description => "Generates a complete workflow skeleton with inputs, stages, and outputs";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            name = new
            {
                type = "string",
                description = "Workflow name"
            },
            description = new
            {
                type = "string",
                description = "Workflow description"
            },
            inputParameters = new
            {
                type = "array",
                description = "List of input parameter names",
                items = new
                {
                    type = "string"
                }
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

        List<string> inputParameters = [];
        if (arguments?.TryGetValue("inputParameters", out var inputParamsObj) == true)
        {
            if (inputParamsObj is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Array)
            {
                inputParameters = JsonSerializer.Deserialize<List<string>>(jsonElement.GetRawText()) ?? [];
            }
            else if (inputParamsObj is List<object> list)
            {
                inputParameters = list.Select(x => x.ToString() ?? "").Where(x => !string.IsNullOrEmpty(x)).ToList();
            }
        }

        var yaml = _generator.GenerateWorkflowSkeleton(name, description, inputParameters);
        return Task.FromResult<object>(new
        {
            name,
            yaml
        });
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
    public string Description => "Generates a mock JSON payload for an API endpoint based on its schema";

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
            endpoint = new
            {
                type = "string",
                description = "Endpoint path"
            },
            httpVerb = new
            {
                type = "string",
                description = "HTTP verb (POST, PUT, PATCH)"
            }
        },
        required = new[] { "version", "apiName", "endpoint", "httpVerb" }
    };

    public async Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var version = arguments?.GetValueOrDefault("version")?.ToString()
            ?? throw new ArgumentException("version is required");
        var apiName = arguments?.GetValueOrDefault("apiName")?.ToString()
            ?? throw new ArgumentException("apiName is required");
        var endpoint = arguments?.GetValueOrDefault("endpoint")?.ToString()
            ?? throw new ArgumentException("endpoint is required");
        var httpVerb = arguments?.GetValueOrDefault("httpVerb")?.ToString()
            ?? throw new ArgumentException("httpVerb is required");

        var payload = await _generator.GenerateMockPayloadAsync(version, apiName, endpoint, httpVerb);
        return new
        {
            endpoint,
            httpVerb,
            payload
        };
    }
}
