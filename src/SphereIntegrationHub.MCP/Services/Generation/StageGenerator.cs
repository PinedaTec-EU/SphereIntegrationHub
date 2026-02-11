using SphereIntegrationHub.MCP.Models;
using SphereIntegrationHub.MCP.Services.Catalog;
using SphereIntegrationHub.MCP.Services.Integration;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SphereIntegrationHub.MCP.Services.Generation;

/// <summary>
/// Generates YAML stage definitions from API endpoints and templates
/// </summary>
public sealed class StageGenerator
{
    private readonly SihServicesAdapter _adapter;
    private readonly SwaggerReader _swaggerReader;
    private readonly ISerializer _yamlSerializer;

    public StageGenerator(SihServicesAdapter adapter)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _swaggerReader = new SwaggerReader(adapter);
        _yamlSerializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults)
            .Build();
    }

    /// <summary>
    /// Generates a stage definition for a specific API endpoint
    /// </summary>
    public async Task<string> GenerateEndpointStageAsync(
        string version,
        string apiName,
        string endpoint,
        string httpVerb,
        string? stageName = null)
    {
        var endpointInfo = await _swaggerReader.GetEndpointSchemaAsync(version, apiName, endpoint, httpVerb);
        if (endpointInfo == null)
        {
            throw new InvalidOperationException($"Endpoint not found: {httpVerb} {endpoint} in {apiName}");
        }

        stageName ??= GenerateStageName(apiName, endpoint, httpVerb);

        var stage = new Dictionary<string, object>
        {
            ["name"] = stageName,
            ["type"] = "api",
            ["api"] = apiName,
            ["endpoint"] = endpoint,
            ["verb"] = httpVerb
        };

        // Add query parameters if present
        if (endpointInfo.QueryParameters.Count > 0)
        {
            var queryParams = new Dictionary<string, string>();
            foreach (var param in endpointInfo.QueryParameters)
            {
                queryParams[param.Name] = param.Required
                    ? $"{{{{ input.{ToCamelCase(param.Name)} }}}}"
                    : param.DefaultValue ?? "";
            }
            stage["queryParams"] = queryParams;
        }

        // Add path parameters if present
        if (endpointInfo.PathParameters.Count > 0)
        {
            var pathParams = new Dictionary<string, string>();
            foreach (var param in endpointInfo.PathParameters)
            {
                pathParams[param.Name] = $"{{{{ input.{ToCamelCase(param.Name)} }}}}";
            }
            stage["pathParams"] = pathParams;
        }

        // Add body if endpoint accepts one
        if (endpointInfo.BodySchema != null && (httpVerb == "POST" || httpVerb == "PUT" || httpVerb == "PATCH"))
        {
            var body = new Dictionary<string, string>();
            foreach (var field in endpointInfo.BodySchema.Fields)
            {
                if (endpointInfo.BodySchema.RequiredFields.Contains(field.Key))
                {
                    body[field.Key] = $"{{{{ input.{ToCamelCase(field.Key)} }}}}";
                }
            }
            stage["body"] = body;
        }

        // Add output capture
        stage["output"] = new Dictionary<string, object>
        {
            ["save"] = true,
            ["context"] = $"{stageName}Result"
        };

        var yaml = _yamlSerializer.Serialize(stage);
        return yaml;
    }

    /// <summary>
    /// Generates a complete workflow skeleton
    /// </summary>
    public string GenerateWorkflowSkeleton(string name, string description, List<string> inputParameters)
    {
        var workflow = new Dictionary<string, object>
        {
            ["name"] = name,
            ["version"] = "1.0",
            ["description"] = description,
            ["input"] = inputParameters.Select(p => new Dictionary<string, object>
            {
                ["name"] = p,
                ["type"] = "string",
                ["required"] = true
            }).ToList(),
            ["stages"] = new List<Dictionary<string, object>>
            {
                new()
                {
                    ["name"] = "example_stage",
                    ["type"] = "api",
                    ["api"] = "your-api-name",
                    ["endpoint"] = "/api/endpoint",
                    ["verb"] = "GET"
                }
            },
            ["end-stage"] = new Dictionary<string, object>
            {
                ["output"] = new Dictionary<string, object>
                {
                    ["result"] = "{{ stages.example_stage.output }}"
                }
            }
        };

        var yaml = _yamlSerializer.Serialize(workflow);
        return yaml;
    }

    /// <summary>
    /// Generates a mock payload for an API endpoint
    /// </summary>
    public async Task<string> GenerateMockPayloadAsync(
        string version,
        string apiName,
        string endpoint,
        string httpVerb)
    {
        var endpointInfo = await _swaggerReader.GetEndpointSchemaAsync(version, apiName, endpoint, httpVerb);
        if (endpointInfo == null)
        {
            throw new InvalidOperationException($"Endpoint not found: {httpVerb} {endpoint} in {apiName}");
        }

        if (endpointInfo.BodySchema == null)
        {
            return "{}";
        }

        var payload = new Dictionary<string, object>();
        foreach (var (fieldName, fieldSchema) in endpointInfo.BodySchema.Fields)
        {
            payload[fieldName] = GenerateMockValue(fieldSchema);
        }

        return System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private static string GenerateStageName(string apiName, string endpoint, string httpVerb)
    {
        // Extract meaningful part from endpoint
        var parts = endpoint.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var mainPart = parts.LastOrDefault(p => !p.StartsWith("{")) ?? parts.LastOrDefault() ?? "endpoint";

        // Create readable name
        var name = $"{httpVerb.ToLower()}_{apiName}_{mainPart}";
        name = System.Text.RegularExpressions.Regex.Replace(name, @"[^a-zA-Z0-9_]", "_");
        return name.ToLowerInvariant();
    }

    private static string ToCamelCase(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        return char.ToLowerInvariant(input[0]) + input[1..];
    }

    private static object GenerateMockValue(FieldSchema schema)
    {
        if (schema.EnumValues != null && schema.EnumValues.Count > 0)
        {
            return schema.EnumValues[0];
        }

        return schema.Type.ToLowerInvariant() switch
        {
            "string" => schema.Format switch
            {
                "date" => DateTime.Now.ToString("yyyy-MM-dd"),
                "date-time" => DateTime.Now.ToString("o"),
                "email" => "user@example.com",
                "uuid" => Guid.NewGuid().ToString(),
                _ => "example_string"
            },
            "integer" or "int32" => 123,
            "long" or "int64" => 123456789L,
            "number" or "float" or "double" => 123.45,
            "boolean" => true,
            "array" => new List<object>(),
            "object" => new Dictionary<string, object>(),
            _ => "value"
        };
    }
}
