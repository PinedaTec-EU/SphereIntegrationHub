using System.Text;
using System.Text.Json;
using SphereIntegrationHub.MCP.Models;
using SphereIntegrationHub.MCP.Services.Catalog;
using SphereIntegrationHub.MCP.Services.Integration;
using YamlDotNet.Serialization;

namespace SphereIntegrationHub.MCP.Services.Generation;

/// <summary>
/// Generates workflow artifacts aligned with SphereIntegrationHub workflow schema.
/// </summary>
public sealed class StageGenerator
{
    private readonly SwaggerReader _swaggerReader;
    private readonly ISerializer _yamlSerializer;

    public StageGenerator(SihServicesAdapter adapter)
    {
        _swaggerReader = new SwaggerReader(adapter ?? throw new ArgumentNullException(nameof(adapter)));
        _yamlSerializer = new SerializerBuilder().Build();
    }

    public async Task<string> GenerateEndpointStageAsync(
        string version,
        string apiName,
        string endpoint,
        string httpVerb,
        string? stageName = null,
        EndpointInfo? fallbackEndpoint = null)
    {
        var endpointInfo = fallbackEndpoint;
        if (endpointInfo == null)
        {
            endpointInfo = await _swaggerReader.GetEndpointSchemaAsync(version, apiName, endpoint, httpVerb);
            if (endpointInfo == null)
            {
                throw new InvalidOperationException($"Endpoint not found: {httpVerb} {endpoint} in {apiName}");
            }
        }

        stageName ??= GenerateStageName(endpointInfo.Endpoint, endpointInfo.HttpVerb);
        var stage = BuildEndpointStage(endpointInfo, stageName);
        return _yamlSerializer.Serialize(stage);
    }

    public string GenerateWorkflowSkeleton(
        string name,
        string description,
        List<string> inputParameters,
        string version)
    {
        var workflow = new Dictionary<string, object?>
        {
            ["version"] = version,
            ["id"] = GenerateWorkflowId(),
            ["name"] = name,
            ["description"] = description,
            ["output"] = true,
            ["input"] = inputParameters.Select(p => new Dictionary<string, object>
            {
                ["name"] = p,
                ["type"] = "Text",
                ["required"] = true
            }).ToList(),
            ["stages"] = new List<Dictionary<string, object?>>
            {
                new()
                {
                    ["name"] = "example-endpoint",
                    ["kind"] = "Endpoint",
                    ["apiRef"] = "api-ref-name",
                    ["endpoint"] = "/api/resource",
                    ["httpVerb"] = "GET",
                    ["expectedStatus"] = 200,
                    ["output"] = new Dictionary<string, string>
                    {
                        ["dto"] = "{{response.body}}",
                        ["http_status"] = "{{response.status}}"
                    }
                }
            },
            ["endStage"] = new Dictionary<string, object>
            {
                ["output"] = new Dictionary<string, string>
                {
                    ["result"] = "{{stage:example-endpoint.output.dto}}"
                }
            }
        };

        return _yamlSerializer.Serialize(workflow);
    }

    public string GenerateWorkflowBundle(
        string name,
        string description,
        string version,
        string apiName,
        IReadOnlyList<Dictionary<string, object?>> stages,
        IReadOnlyCollection<string> inputNames)
    {
        var workflow = new Dictionary<string, object?>
        {
            ["version"] = version,
            ["id"] = GenerateWorkflowId(),
            ["name"] = name,
            ["description"] = description,
            ["output"] = true,
            ["references"] = new Dictionary<string, object?>
            {
                ["apis"] = new List<object>
                {
                    new Dictionary<string, string>
                    {
                        ["name"] = apiName,
                        ["definition"] = apiName
                    }
                }
            },
            ["input"] = inputNames.Select(input => new Dictionary<string, object>
            {
                ["name"] = input,
                ["type"] = "Text",
                ["required"] = true
            }).ToList(),
            ["stages"] = stages.ToList(),
            ["endStage"] = new Dictionary<string, object?>
            {
                ["output"] = new Dictionary<string, string>
                {
                    ["last_response"] = stages.Count > 0
                        ? $"{{{{stage:{stages.Last()["name"]}.output.dto}}}}"
                        : string.Empty
                }
            }
        };

        return _yamlSerializer.Serialize(workflow);
    }

    public string GenerateWfvars(IReadOnlyCollection<string> inputNames)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var inputName in inputNames)
        {
            map[inputName] = $"<set-{inputName}>";
        }

        return _yamlSerializer.Serialize(map);
    }

    public async Task<string> GenerateMockPayloadAsync(
        string version,
        string apiName,
        string endpoint,
        string httpVerb,
        EndpointInfo? fallbackEndpoint = null)
    {
        var endpointInfo = fallbackEndpoint;
        if (endpointInfo == null)
        {
            endpointInfo = await _swaggerReader.GetEndpointSchemaAsync(version, apiName, endpoint, httpVerb);
            if (endpointInfo == null)
            {
                throw new InvalidOperationException($"Endpoint not found: {httpVerb} {endpoint} in {apiName}");
            }
        }

        if (endpointInfo.BodySchema == null)
        {
            return "{}";
        }

        var payload = new Dictionary<string, object?>();
        foreach (var (fieldName, fieldSchema) in endpointInfo.BodySchema.Fields)
        {
            payload[fieldName] = GenerateMockValue(fieldSchema);
        }

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    public Dictionary<string, object?> BuildEndpointStage(EndpointInfo endpointInfo, string stageName)
    {
        var hasBody = endpointInfo.BodySchema != null &&
            (endpointInfo.HttpVerb is "POST" or "PUT" or "PATCH");

        var stage = new Dictionary<string, object?>
        {
            ["name"] = stageName,
            ["kind"] = "Endpoint",
            ["apiRef"] = endpointInfo.ApiName,
            ["endpoint"] = ApplyPathTemplates(endpointInfo.Endpoint, endpointInfo.PathParameters),
            ["httpVerb"] = endpointInfo.HttpVerb,
            ["expectedStatus"] = GetExpectedStatus(endpointInfo),
            ["output"] = new Dictionary<string, string>
            {
                ["dto"] = "{{response.body}}",
                ["http_status"] = "{{response.status}}"
            }
        };

        if (hasBody)
        {
            stage["headers"] = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json"
            };
            stage["body"] = BuildBodyTemplate(endpointInfo.BodySchema!);
        }

        if (endpointInfo.QueryParameters.Count > 0)
        {
            var query = endpointInfo.QueryParameters.ToDictionary(
                p => p.Name,
                p => p.Required
                    ? $"{{{{input.{ToTokenName(p.Name)}}}}}"
                    : p.DefaultValue ?? string.Empty);
            stage["query"] = query;
        }

        return stage;
    }

    private static string BuildBodyTemplate(BodySchema bodySchema)
    {
        var json = new StringBuilder();
        json.AppendLine("{");
        var required = bodySchema.RequiredFields.Count > 0
            ? bodySchema.RequiredFields
            : bodySchema.Fields.Keys.ToList();

        for (var index = 0; index < required.Count; index++)
        {
            var key = required[index];
            var comma = index == required.Count - 1 ? string.Empty : ",";
            json.Append("  \"")
                .Append(key)
                .Append("\": \"{{input.")
                .Append(ToTokenName(key))
                .Append("}}\"")
                .Append(comma)
                .AppendLine();
        }

        json.Append('}');
        return json.ToString();
    }

    private static int GetExpectedStatus(EndpointInfo endpointInfo)
    {
        if (endpointInfo.Responses.ContainsKey(200))
        {
            return 200;
        }

        if (endpointInfo.Responses.ContainsKey(201))
        {
            return 201;
        }

        if (endpointInfo.Responses.Count > 0)
        {
            return endpointInfo.Responses.Keys.OrderBy(x => x).First();
        }

        return endpointInfo.HttpVerb switch
        {
            "POST" => 201,
            _ => 200
        };
    }

    private static string ApplyPathTemplates(string endpoint, IReadOnlyCollection<ParameterInfo> pathParameters)
    {
        var resolved = endpoint;
        foreach (var pathParameter in pathParameters)
        {
            resolved = resolved.Replace(
                $"{{{pathParameter.Name}}}",
                $"{{{{input.{ToTokenName(pathParameter.Name)}}}}}",
                StringComparison.OrdinalIgnoreCase);
        }

        return resolved;
    }

    private static string GenerateStageName(string endpoint, string httpVerb)
    {
        var parts = endpoint.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var meaningful = parts.LastOrDefault(part => !part.StartsWith("{", StringComparison.Ordinal));
        meaningful ??= "endpoint";
        var name = $"{httpVerb}_{meaningful}".ToLowerInvariant();
        return System.Text.RegularExpressions.Regex.Replace(name, @"[^a-z0-9_]", "_");
    }

    private static string GenerateWorkflowId()
    {
        return Guid.NewGuid().ToString("N").ToUpperInvariant();
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

    private static object? GenerateMockValue(FieldSchema schema)
    {
        if (schema.EnumValues != null && schema.EnumValues.Count > 0)
        {
            return schema.EnumValues[0];
        }

        return schema.Type.ToLowerInvariant() switch
        {
            "string" => schema.Format switch
            {
                "date" => DateTime.UtcNow.ToString("yyyy-MM-dd"),
                "date-time" => DateTime.UtcNow.ToString("o"),
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
