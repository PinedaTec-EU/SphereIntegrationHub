using System.Text.Json;
using System.Text.Json.Serialization;
using SphereIntegrationHub.MCP.Models;
using SphereIntegrationHub.MCP.Services.Integration;

namespace SphereIntegrationHub.MCP.Services.Catalog;

/// <summary>
/// Reads cached Swagger files and extracts endpoint information
/// </summary>
public sealed class SwaggerReader
{
    private readonly SihServicesAdapter _adapter;

    public SwaggerReader(SihServicesAdapter adapter)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    }

    /// <summary>
    /// Gets all endpoints from a cached Swagger file
    /// </summary>
    public async Task<List<EndpointInfo>> GetEndpointsAsync(string version, string apiName)
    {
        var swaggerPath = _adapter.GetSwaggerCachePath(version, apiName);
        if (!File.Exists(swaggerPath))
        {
            throw new FileNotFoundException($"Swagger cache not found: {swaggerPath}");
        }

        var json = await File.ReadAllTextAsync(swaggerPath);
        var swagger = JsonSerializer.Deserialize<SwaggerDocument>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (swagger == null || swagger.Paths == null)
        {
            return [];
        }

        var endpoints = new List<EndpointInfo>();

        foreach (var (path, pathItem) in swagger.Paths)
        {
            if (pathItem.Get != null)
            {
                endpoints.Add(CreateEndpointInfo(apiName, path, "GET", pathItem.Get, swagger.Definitions));
            }
            if (pathItem.Post != null)
            {
                endpoints.Add(CreateEndpointInfo(apiName, path, "POST", pathItem.Post, swagger.Definitions));
            }
            if (pathItem.Put != null)
            {
                endpoints.Add(CreateEndpointInfo(apiName, path, "PUT", pathItem.Put, swagger.Definitions));
            }
            if (pathItem.Delete != null)
            {
                endpoints.Add(CreateEndpointInfo(apiName, path, "DELETE", pathItem.Delete, swagger.Definitions));
            }
            if (pathItem.Patch != null)
            {
                endpoints.Add(CreateEndpointInfo(apiName, path, "PATCH", pathItem.Patch, swagger.Definitions));
            }
        }

        return endpoints;
    }

    /// <summary>
    /// Gets detailed schema for a specific endpoint
    /// </summary>
    public async Task<EndpointInfo?> GetEndpointSchemaAsync(string version, string apiName, string endpoint, string httpVerb)
    {
        var endpoints = await GetEndpointsAsync(version, apiName);
        return endpoints.FirstOrDefault(e =>
            e.Endpoint.Equals(endpoint, StringComparison.OrdinalIgnoreCase) &&
            e.HttpVerb.Equals(httpVerb, StringComparison.OrdinalIgnoreCase));
    }

    private static EndpointInfo CreateEndpointInfo(
        string apiName,
        string path,
        string verb,
        SwaggerOperation operation,
        Dictionary<string, SwaggerSchema>? definitions)
    {
        var queryParams = new List<ParameterInfo>();
        var headerParams = new List<ParameterInfo>();
        var pathParams = new List<ParameterInfo>();
        BodySchema? bodySchema = null;

        if (operation.Parameters != null)
        {
            foreach (var param in operation.Parameters)
            {
                var paramInfo = new ParameterInfo
                {
                    Name = param.Name ?? "",
                    Type = param.Type ?? param.Schema?.Type ?? "object",
                    Required = param.Required,
                    Description = param.Description,
                    DefaultValue = param.Default?.ToString()
                };

                switch (param.In?.ToLowerInvariant())
                {
                    case "query":
                        queryParams.Add(paramInfo);
                        break;
                    case "header":
                        headerParams.Add(paramInfo);
                        break;
                    case "path":
                        pathParams.Add(paramInfo);
                        break;
                    case "body":
                        if (param.Schema != null)
                        {
                            bodySchema = ExtractBodySchema(param.Schema, definitions);
                        }
                        break;
                }
            }
        }

        var responses = new Dictionary<int, ResponseSchema>();
        if (operation.Responses != null)
        {
            foreach (var (code, response) in operation.Responses)
            {
                if (int.TryParse(code, out var statusCode))
                {
                    responses[statusCode] = new ResponseSchema
                    {
                        StatusCode = statusCode,
                        Description = response.Description ?? "",
                        Fields = response.Schema != null ? ExtractFields(response.Schema, definitions) : null
                    };
                }
            }
        }

        return new EndpointInfo
        {
            ApiName = apiName,
            Endpoint = path,
            HttpVerb = verb,
            Summary = operation.Summary ?? "",
            Description = operation.Description ?? operation.Summary ?? "",
            QueryParameters = queryParams,
            HeaderParameters = headerParams,
            PathParameters = pathParams,
            BodySchema = bodySchema,
            Responses = responses,
            Tags = operation.Tags ?? []
        };
    }

    private static BodySchema? ExtractBodySchema(SwaggerSchema schema, Dictionary<string, SwaggerSchema>? definitions)
    {
        var fields = ExtractFields(schema, definitions);
        if (fields == null)
        {
            return null;
        }

        return new BodySchema
        {
            Fields = fields,
            RequiredFields = schema.Required ?? []
        };
    }

    private static Dictionary<string, FieldSchema>? ExtractFields(SwaggerSchema schema, Dictionary<string, SwaggerSchema>? definitions)
    {
        // Handle $ref
        if (!string.IsNullOrEmpty(schema.Ref))
        {
            var refName = schema.Ref.Split('/').Last();
            if (definitions?.TryGetValue(refName, out var refSchema) == true)
            {
                return ExtractFields(refSchema, definitions);
            }
        }

        if (schema.Properties == null)
        {
            return null;
        }

        var fields = new Dictionary<string, FieldSchema>();
        foreach (var (name, prop) in schema.Properties)
        {
            fields[name] = new FieldSchema
            {
                Type = prop.Type ?? "object",
                Format = prop.Format,
                Description = prop.Description,
                IsArray = prop.Type == "array",
                EnumValues = prop.Enum
            };
        }

        return fields;
    }
}

// Swagger document models
internal sealed class SwaggerDocument
{
    public Dictionary<string, SwaggerPathItem>? Paths { get; set; }
    public Dictionary<string, SwaggerSchema>? Definitions { get; set; }
}

internal sealed class SwaggerPathItem
{
    public SwaggerOperation? Get { get; set; }
    public SwaggerOperation? Post { get; set; }
    public SwaggerOperation? Put { get; set; }
    public SwaggerOperation? Delete { get; set; }
    public SwaggerOperation? Patch { get; set; }
}

internal sealed class SwaggerOperation
{
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public List<string>? Tags { get; set; }
    public List<SwaggerParameter>? Parameters { get; set; }
    public Dictionary<string, SwaggerResponse>? Responses { get; set; }
}

internal sealed class SwaggerParameter
{
    public string? Name { get; set; }
    public string? In { get; set; }
    public string? Type { get; set; }
    public bool Required { get; set; }
    public string? Description { get; set; }
    public SwaggerSchema? Schema { get; set; }
    public object? Default { get; set; }
}

internal sealed class SwaggerResponse
{
    public string? Description { get; set; }
    public SwaggerSchema? Schema { get; set; }
}

internal sealed class SwaggerSchema
{
    public string? Type { get; set; }
    public string? Format { get; set; }
    public string? Description { get; set; }
    public string? Ref { get; set; }
    public Dictionary<string, SwaggerSchema>? Properties { get; set; }
    public List<string>? Required { get; set; }
    public List<string>? Enum { get; set; }

    [JsonPropertyName("$ref")]
    public string? RefProperty
    {
        get => Ref;
        set => Ref = value;
    }
}
