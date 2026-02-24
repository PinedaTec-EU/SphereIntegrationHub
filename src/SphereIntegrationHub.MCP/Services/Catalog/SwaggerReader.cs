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

        var definitions = ResolveDefinitions(swagger);

        foreach (var (path, pathItem) in swagger.Paths)
        {
            if (pathItem.Get != null)
            {
                endpoints.Add(CreateEndpointInfo(apiName, path, "GET", pathItem.Get, definitions, swagger.Components?.SecuritySchemes, swagger.Security));
            }
            if (pathItem.Post != null)
            {
                endpoints.Add(CreateEndpointInfo(apiName, path, "POST", pathItem.Post, definitions, swagger.Components?.SecuritySchemes, swagger.Security));
            }
            if (pathItem.Put != null)
            {
                endpoints.Add(CreateEndpointInfo(apiName, path, "PUT", pathItem.Put, definitions, swagger.Components?.SecuritySchemes, swagger.Security));
            }
            if (pathItem.Delete != null)
            {
                endpoints.Add(CreateEndpointInfo(apiName, path, "DELETE", pathItem.Delete, definitions, swagger.Components?.SecuritySchemes, swagger.Security));
            }
            if (pathItem.Patch != null)
            {
                endpoints.Add(CreateEndpointInfo(apiName, path, "PATCH", pathItem.Patch, definitions, swagger.Components?.SecuritySchemes, swagger.Security));
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
        Dictionary<string, SwaggerSchema>? definitions,
        Dictionary<string, SwaggerSecurityScheme>? securitySchemes,
        List<Dictionary<string, List<string>>>? rootSecurity)
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

        if (operation.RequestBody?.Content != null)
        {
            var jsonContent = SelectJsonRequestBodyContent(operation.RequestBody.Content);
            if (jsonContent?.Schema != null)
            {
                bodySchema = ExtractBodySchema(jsonContent.Schema, definitions);
                if (bodySchema != null)
                {
                    bodySchema = bodySchema with
                    {
                        Example = ResolveRequestBodyExample(jsonContent)
                    };
                }
            }
        }

        AddSecurityHeaders(operation, securitySchemes, rootSecurity, headerParams);

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

    private static SwaggerMediaType? SelectJsonRequestBodyContent(Dictionary<string, SwaggerMediaType> content)
    {
        if (content.Count == 0)
        {
            return null;
        }

        if (content.TryGetValue("application/json", out var jsonContent))
        {
            return jsonContent;
        }

        return content
            .FirstOrDefault(kvp => kvp.Key.Contains("json", StringComparison.OrdinalIgnoreCase))
            .Value
            ?? content.Values.FirstOrDefault();
    }

    private static string? ResolveRequestBodyExample(SwaggerMediaType mediaType)
    {
        if (mediaType.Example.HasValue)
        {
            return mediaType.Example.Value.GetRawText();
        }

        if (mediaType.Examples != null && mediaType.Examples.Count > 0)
        {
            var first = mediaType.Examples.Values.FirstOrDefault();
            if (first?.Value.HasValue == true)
            {
                return first.Value.Value.GetRawText();
            }
        }

        if (mediaType.Schema?.Example.HasValue == true)
        {
            return mediaType.Schema.Example.Value.GetRawText();
        }

        return null;
    }

    private static Dictionary<string, SwaggerSchema>? ResolveDefinitions(SwaggerDocument swagger)
    {
        if (swagger.Definitions != null && swagger.Definitions.Count > 0)
        {
            return swagger.Definitions;
        }

        if (swagger.Components?.Schemas != null && swagger.Components.Schemas.Count > 0)
        {
            return swagger.Components.Schemas;
        }

        return null;
    }

    private static void AddSecurityHeaders(
        SwaggerOperation operation,
        Dictionary<string, SwaggerSecurityScheme>? securitySchemes,
        List<Dictionary<string, List<string>>>? rootSecurity,
        List<ParameterInfo> headerParams)
    {
        if (securitySchemes == null || securitySchemes.Count == 0)
        {
            return;
        }

        var effectiveSecurity = operation.Security ?? rootSecurity;
        if (effectiveSecurity == null || effectiveSecurity.Count == 0)
        {
            return;
        }

        var existingHeaders = headerParams
            .Select(p => p.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var requirement in effectiveSecurity)
        {
            foreach (var schemeName in requirement.Keys)
            {
                if (!securitySchemes.TryGetValue(schemeName, out var scheme) || scheme == null)
                {
                    continue;
                }

                if (TryResolveHeaderName(scheme, out var headerName) &&
                    existingHeaders.Add(headerName))
                {
                    headerParams.Add(new ParameterInfo
                    {
                        Name = headerName,
                        Type = "string",
                        Required = true,
                        Description = $"Required by security scheme '{schemeName}'."
                    });
                }
            }
        }
    }

    private static bool TryResolveHeaderName(SwaggerSecurityScheme scheme, out string headerName)
    {
        headerName = string.Empty;

        if (scheme.Type?.Equals("apiKey", StringComparison.OrdinalIgnoreCase) == true &&
            scheme.In?.Equals("header", StringComparison.OrdinalIgnoreCase) == true &&
            !string.IsNullOrWhiteSpace(scheme.Name))
        {
            headerName = scheme.Name;
            return true;
        }

        if (scheme.Type?.Equals("http", StringComparison.OrdinalIgnoreCase) == true &&
            scheme.Scheme?.Equals("bearer", StringComparison.OrdinalIgnoreCase) == true)
        {
            headerName = "Authorization";
            return true;
        }

        return false;
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
                EnumValues = prop.Enum?.Select(x => x.ValueKind == JsonValueKind.String
                    ? x.GetString() ?? string.Empty
                    : x.ToString()).ToList()
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
    public SwaggerComponents? Components { get; set; }
    public List<Dictionary<string, List<string>>>? Security { get; set; }
}

internal sealed class SwaggerComponents
{
    public Dictionary<string, SwaggerSecurityScheme>? SecuritySchemes { get; set; }
    public Dictionary<string, SwaggerSchema>? Schemas { get; set; }
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
    public SwaggerRequestBody? RequestBody { get; set; }
    public Dictionary<string, SwaggerResponse>? Responses { get; set; }
    public List<Dictionary<string, List<string>>>? Security { get; set; }
}

internal sealed class SwaggerRequestBody
{
    public bool Required { get; set; }
    public Dictionary<string, SwaggerMediaType>? Content { get; set; }
}

internal sealed class SwaggerMediaType
{
    public SwaggerSchema? Schema { get; set; }
    public JsonElement? Example { get; set; }
    public Dictionary<string, SwaggerNamedExample>? Examples { get; set; }
}

internal sealed class SwaggerNamedExample
{
    public JsonElement? Value { get; set; }
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
    public List<JsonElement>? Enum { get; set; }
    public JsonElement? Example { get; set; }

    [JsonPropertyName("$ref")]
    public string? RefProperty
    {
        get => Ref;
        set => Ref = value;
    }
}

internal sealed class SwaggerSecurityScheme
{
    public string? Type { get; set; }
    public string? In { get; set; }
    public string? Name { get; set; }
    public string? Scheme { get; set; }
}
