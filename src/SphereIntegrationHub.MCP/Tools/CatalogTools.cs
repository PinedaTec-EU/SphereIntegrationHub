using SphereIntegrationHub.MCP.Core;
using SphereIntegrationHub.MCP.Services.Catalog;
using SphereIntegrationHub.MCP.Services.Integration;

namespace SphereIntegrationHub.MCP.Tools;

/// <summary>
/// Lists all available API catalog versions
/// </summary>
[McpTool("list_api_catalog_versions", "Lists all available API catalog versions", Category = "Catalog", Level = "L1")]
public sealed class ListApiCatalogVersionsTool : IMcpTool
{
    private readonly ApiCatalogReader _catalogReader;

    public ListApiCatalogVersionsTool(SihServicesAdapter adapter)
    {
        _catalogReader = new ApiCatalogReader(adapter);
    }

    public string Name => "list_api_catalog_versions";
    public string Description => "Lists all available API catalog versions";

    public object InputSchema => new
    {
        type = "object",
        properties = new { },
        required = Array.Empty<string>()
    };

    public async Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var versions = await _catalogReader.GetVersionsAsync();
        return new
        {
            versions,
            count = versions.Count
        };
    }
}

/// <summary>
/// Gets API definitions for a specific version
/// </summary>
[McpTool("get_api_definitions", "Gets all API definitions for a specific catalog version", Category = "Catalog", Level = "L1")]
public sealed class GetApiDefinitionsTool : IMcpTool
{
    private readonly ApiCatalogReader _catalogReader;

    public GetApiDefinitionsTool(SihServicesAdapter adapter)
    {
        _catalogReader = new ApiCatalogReader(adapter);
    }

    public string Name => "get_api_definitions";
    public string Description => "Gets all API definitions for a specific catalog version";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            version = new
            {
                type = "string",
                description = "API catalog version (e.g., '3.10', '3.11')"
            }
        },
        required = new[] { "version" }
    };

    public async Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var version = arguments?.GetValueOrDefault("version")?.ToString()
            ?? throw new ArgumentException("version is required");

        var definitions = await _catalogReader.GetApiDefinitionsAsync(version);
        return new
        {
            version,
            apis = definitions.Select(d => new
            {
                d.Name,
                d.BasePath,
                d.SwaggerUrl
            }).ToList(),
            count = definitions.Count
        };
    }
}

/// <summary>
/// Gets all endpoints from a specific API
/// </summary>
[McpTool("get_api_endpoints", "Gets all endpoints from a specific API", Category = "Catalog", Level = "L1")]
public sealed class GetApiEndpointsTool : IMcpTool
{
    private readonly SwaggerReader _swaggerReader;

    public GetApiEndpointsTool(SihServicesAdapter adapter)
    {
        _swaggerReader = new SwaggerReader(adapter);
    }

    public string Name => "get_api_endpoints";
    public string Description => "Gets all endpoints from a specific API";

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

        var endpoints = await _swaggerReader.GetEndpointsAsync(version, apiName);
        return new
        {
            version,
            apiName,
            endpoints = endpoints.Select(e => new
            {
                e.Endpoint,
                e.HttpVerb,
                e.Summary,
                e.Tags,
                queryParameterCount = e.QueryParameters.Count,
                pathParameterCount = e.PathParameters.Count,
                hasBody = e.BodySchema != null
            }).ToList(),
            count = endpoints.Count
        };
    }
}

/// <summary>
/// Gets detailed schema for a specific endpoint
/// </summary>
[McpTool("get_endpoint_schema", "Gets detailed schema for a specific API endpoint", Category = "Catalog", Level = "L1")]
public sealed class GetEndpointSchemaTool : IMcpTool
{
    private readonly SwaggerReader _swaggerReader;

    public GetEndpointSchemaTool(SihServicesAdapter adapter)
    {
        _swaggerReader = new SwaggerReader(adapter);
    }

    public string Name => "get_endpoint_schema";
    public string Description => "Gets detailed schema for a specific API endpoint including parameters, body, and response schemas";

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
                description = "Endpoint path (e.g., '/api/accounts')"
            },
            httpVerb = new
            {
                type = "string",
                description = "HTTP verb (GET, POST, PUT, DELETE, PATCH)"
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

        var endpointInfo = await _swaggerReader.GetEndpointSchemaAsync(version, apiName, endpoint, httpVerb);
        if (endpointInfo == null)
        {
            throw new InvalidOperationException($"Endpoint not found: {httpVerb} {endpoint} in {apiName}");
        }

        // Return as camelCase anonymous object for test compatibility
        return new
        {
            apiName = endpointInfo.ApiName,
            endpoint = endpointInfo.Endpoint,
            httpVerb = endpointInfo.HttpVerb,
            summary = endpointInfo.Summary,
            description = endpointInfo.Description,
            tags = endpointInfo.Tags,
            queryParameters = endpointInfo.QueryParameters?.Select(p => new
            {
                name = p.Name,
                type = p.Type,
                required = p.Required,
                description = p.Description,
                defaultValue = p.DefaultValue
            }).ToList(),
            pathParameters = endpointInfo.PathParameters?.Select(p => new
            {
                name = p.Name,
                type = p.Type,
                required = p.Required,
                description = p.Description,
                defaultValue = p.DefaultValue
            }).ToList(),
            headerParameters = endpointInfo.HeaderParameters?.Select(p => new
            {
                name = p.Name,
                type = p.Type,
                required = p.Required,
                description = p.Description,
                defaultValue = p.DefaultValue
            }).ToList(),
            requestBody = endpointInfo.BodySchema != null ? new
            {
                fields = endpointInfo.BodySchema.Fields,
                requiredFields = endpointInfo.BodySchema.RequiredFields
            } : null,
            responses = endpointInfo.Responses?.Select(r => new
            {
                statusCode = r.Value.StatusCode,
                description = r.Value.Description,
                fields = r.Value.Fields
            }).ToList()
        };
    }
}
