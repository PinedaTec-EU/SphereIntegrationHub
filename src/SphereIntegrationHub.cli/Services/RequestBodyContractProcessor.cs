using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.Services;

public interface IRequestBodyContractProcessor
{
    string Process(WorkflowStageDefinition stage, string body);
}

internal sealed class NoOpRequestBodyContractProcessor : IRequestBodyContractProcessor
{
    public static IRequestBodyContractProcessor Instance { get; } = new NoOpRequestBodyContractProcessor();

    public string Process(WorkflowStageDefinition stage, string body) => body;
}

internal sealed class RequestBodyContractProcessor : IRequestBodyContractProcessor
{
    private readonly RequestContractRegistry _registry;

    public RequestBodyContractProcessor(RequestContractRegistry registry)
    {
        _registry = registry;
    }

    public string Process(WorkflowStageDefinition stage, string body)
    {
        if (string.IsNullOrWhiteSpace(body) || !_registry.TryGetBodyContract(stage, out var contract))
        {
            return body;
        }

        JsonNode? bodyNode;
        try
        {
            bodyNode = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return body;
        }

        if (bodyNode is null)
        {
            return body;
        }

        ValidateNode(stage, "$", bodyNode, contract);
        return bodyNode.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    private static void ValidateNode(WorkflowStageDefinition stage, string path, JsonNode? node, RequestBodySchema schema)
    {
        if (node is null)
        {
            if (schema.Required)
            {
                throw BuildValidationException(stage, path, "is required.");
            }

            return;
        }

        switch (schema.Type)
        {
            case "object":
                ValidateObject(stage, path, node, schema);
                return;
            case "array":
                ValidateArray(stage, path, node, schema);
                return;
            default:
                ValidateScalar(stage, path, node, schema);
                return;
        }
    }

    private static void ValidateObject(WorkflowStageDefinition stage, string path, JsonNode node, RequestBodySchema schema)
    {
        if (node is not JsonObject jsonObject)
        {
            throw BuildValidationException(stage, path, $"expected object but got {DescribeNodeType(node)}.");
        }

        foreach (var requiredField in schema.RequiredFields)
        {
            if (!jsonObject.ContainsKey(requiredField))
            {
                throw BuildValidationException(stage, $"{path}.{requiredField}", "is required.");
            }
        }

        foreach (var property in jsonObject.ToList())
        {
            if (property.Key is null || !schema.Properties.TryGetValue(property.Key, out var propertySchema))
            {
                continue;
            }

            ValidateNode(stage, $"{path}.{property.Key}", property.Value, propertySchema);
        }
    }

    private static void ValidateArray(WorkflowStageDefinition stage, string path, JsonNode node, RequestBodySchema schema)
    {
        if (node is not JsonArray jsonArray)
        {
            throw BuildValidationException(stage, path, $"expected array but got {DescribeNodeType(node)}.");
        }

        if (schema.Items is null)
        {
            return;
        }

        for (var index = 0; index < jsonArray.Count; index++)
        {
            ValidateNode(stage, $"{path}[{index}]", jsonArray[index], schema.Items);
        }
    }

    private static void ValidateScalar(WorkflowStageDefinition stage, string path, JsonNode node, RequestBodySchema schema)
    {
        if (node is not JsonValue valueNode)
        {
            throw BuildValidationException(stage, path, $"expected {DescribeExpectedType(schema)} but got {DescribeNodeType(node)}.");
        }

        if (TryNormalizeEnumByName(valueNode, schema, out var normalizedNode))
        {
            ReplaceValue(node, normalizedNode);
            valueNode = normalizedNode;
        }

        if (!IsTypeCompatible(valueNode, schema.Type))
        {
            throw BuildValidationException(
                stage,
                path,
                $"expected {DescribeExpectedType(schema)} but got {DescribeNodeType(valueNode)}.");
        }

        if (schema.EnumValues.Count > 0 && !MatchesEnumValue(valueNode, schema.EnumValues))
        {
            var allowed = schema.EnumNames.Count > 0
                ? string.Join(", ", schema.EnumNames)
                : string.Join(", ", schema.EnumValues.Select(DescribeJson));
            throw BuildValidationException(
                stage,
                path,
                $"expected one of [{allowed}] but got {DescribeJsonNodeValue(valueNode)}.");
        }
    }

    private static bool TryNormalizeEnumByName(JsonValue valueNode, RequestBodySchema schema, out JsonValue normalizedNode)
    {
        normalizedNode = valueNode;
        if (schema.EnumNames.Count == 0 || schema.EnumValues.Count == 0)
        {
            return false;
        }

        if (!TryGetString(valueNode, out var rawValue))
        {
            return false;
        }

        for (var index = 0; index < schema.EnumNames.Count && index < schema.EnumValues.Count; index++)
        {
            if (!string.Equals(schema.EnumNames[index], rawValue, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            normalizedNode = CreateValueNode(schema.EnumValues[index]);
            return true;
        }

        return false;
    }

    private static void ReplaceValue(JsonNode currentNode, JsonValue normalizedNode)
    {
        var parent = currentNode.Parent;
        switch (parent)
        {
            case JsonObject parentObject:
            {
                var property = parentObject.First(pair => ReferenceEquals(pair.Value, currentNode)).Key;
                parentObject[property] = normalizedNode;
                break;
            }
            case JsonArray parentArray:
            {
                for (var index = 0; index < parentArray.Count; index++)
                {
                    if (!ReferenceEquals(parentArray[index], currentNode))
                    {
                        continue;
                    }

                    parentArray[index] = normalizedNode;
                    break;
                }

                break;
            }
        }
    }

    private static bool IsTypeCompatible(JsonValue valueNode, string expectedType)
    {
        if (!TryGetJsonElement(valueNode, out var element))
        {
            return false;
        }

        return expectedType switch
        {
            "string" => element.ValueKind == JsonValueKind.String,
            "integer" => element.ValueKind == JsonValueKind.Number && !element.ToString().Contains('.', StringComparison.Ordinal),
            "number" => element.ValueKind == JsonValueKind.Number,
            "boolean" => element.ValueKind is JsonValueKind.True or JsonValueKind.False,
            _ => true
        };
    }

    private static bool MatchesEnumValue(JsonValue valueNode, IReadOnlyList<JsonElement> enumValues)
    {
        if (!TryGetJsonElement(valueNode, out var element))
        {
            return false;
        }

        return enumValues.Any(candidate => candidate.GetRawText() == element.GetRawText());
    }

    private static bool TryGetString(JsonValue valueNode, out string value)
    {
        if (valueNode.TryGetValue<string>(out var stringValue) && stringValue is not null)
        {
            value = stringValue;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static JsonValue CreateValueNode(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => JsonValue.Create(element.GetString()),
            JsonValueKind.Number when element.TryGetInt64(out var intValue) => JsonValue.Create(intValue),
            JsonValueKind.Number when element.TryGetDecimal(out var decimalValue) => JsonValue.Create(decimalValue),
            JsonValueKind.True => JsonValue.Create(true),
            JsonValueKind.False => JsonValue.Create(false),
            _ => JsonValue.Create(element.GetRawText())
        } ?? throw new InvalidOperationException("Failed to create JSON value node.");
    }

    private static string DescribeExpectedType(RequestBodySchema schema)
    {
        if (schema.EnumNames.Count > 0)
        {
            return $"{schema.Type} enum";
        }

        return schema.Type;
    }

    private static string DescribeNodeType(JsonNode node)
    {
        return node switch
        {
            JsonObject => "object",
            JsonArray => "array",
            JsonValue valueNode when TryGetJsonElement(valueNode, out var element) => element.ValueKind switch
            {
                JsonValueKind.String => "string",
                JsonValueKind.Number => "number",
                JsonValueKind.True or JsonValueKind.False => "boolean",
                JsonValueKind.Null => "null",
                _ => element.ValueKind.ToString().ToLowerInvariant()
            },
            _ => "unknown"
        };
    }

    private static string DescribeJson(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : value.ToString();
    }

    private static string DescribeJsonNodeValue(JsonValue valueNode)
    {
        return TryGetJsonElement(valueNode, out var element)
            ? DescribeJson(element)
            : valueNode.ToJsonString();
    }

    private static bool TryGetJsonElement(JsonNode node, out JsonElement element)
    {
        try
        {
            using var document = JsonDocument.Parse(node.ToJsonString());
            element = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            element = default;
            return false;
        }
    }

    private static InvalidOperationException BuildValidationException(WorkflowStageDefinition stage, string path, string detail)
    {
        return new InvalidOperationException($"Stage '{stage.Name}' request body field '{path}' {detail}");
    }
}

internal sealed class RequestContractRegistry
{
    private readonly Dictionary<string, Dictionary<string, RequestBodySchema>> _contractsByApi;

    private RequestContractRegistry(Dictionary<string, Dictionary<string, RequestBodySchema>> contractsByApi)
    {
        _contractsByApi = contractsByApi;
    }

    public static RequestContractRegistry Load(
        WorkflowDefinition workflow,
        ApiCatalogVersion catalogVersion,
        string cacheRoot)
    {
        var contracts = new Dictionary<string, Dictionary<string, RequestBodySchema>>(StringComparer.OrdinalIgnoreCase);
        var apiLookup = workflow.References?.Apis?.ToDictionary(item => item.Name, item => item.Definition, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var definitionName in apiLookup.Values.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var definition = catalogVersion.Definitions.FirstOrDefault(item => item.Name.Equals(definitionName, StringComparison.OrdinalIgnoreCase));
            if (definition is null)
            {
                continue;
            }

            var swaggerPath = Path.Combine(cacheRoot, $"{definition.Name}.json");
            if (!File.Exists(swaggerPath))
            {
                continue;
            }

            var json = File.ReadAllText(swaggerPath);
            var swagger = JsonSerializer.Deserialize<SwaggerDocumentContract>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (swagger?.Paths is null)
            {
                continue;
            }

            var definitionsLookup = swagger.Definitions ?? swagger.Components?.Schemas;
            var operationMap = new Dictionary<string, RequestBodySchema>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in swagger.Paths)
            {
                AddOperationContract(operationMap, path.Key, "get", path.Value.Get, definitionsLookup);
                AddOperationContract(operationMap, path.Key, "post", path.Value.Post, definitionsLookup);
                AddOperationContract(operationMap, path.Key, "put", path.Value.Put, definitionsLookup);
                AddOperationContract(operationMap, path.Key, "patch", path.Value.Patch, definitionsLookup);
                AddOperationContract(operationMap, path.Key, "delete", path.Value.Delete, definitionsLookup);
            }

            contracts[definition.Name] = operationMap;
        }

        return new RequestContractRegistry(contracts);
    }

    public bool TryGetBodyContract(WorkflowStageDefinition stage, out RequestBodySchema contract)
    {
        contract = null!;
        if (string.IsNullOrWhiteSpace(stage.ApiRef) || string.IsNullOrWhiteSpace(stage.Endpoint) || string.IsNullOrWhiteSpace(stage.HttpVerb))
        {
            return false;
        }

        if (!_contractsByApi.TryGetValue(stage.ApiRef, out var apiContracts))
        {
            return false;
        }

        var key = BuildOperationKey(stage.Endpoint, stage.HttpVerb);
        if (apiContracts.TryGetValue(key, out contract!))
        {
            return true;
        }

        var normalizedEndpoint = NormalizePath(stage.Endpoint);
        var fallbackKey = BuildOperationKey(normalizedEndpoint, stage.HttpVerb);
        return apiContracts.TryGetValue(fallbackKey, out contract!);
    }

    private static void AddOperationContract(
        Dictionary<string, RequestBodySchema> operationMap,
        string path,
        string verb,
        SwaggerOperationContract? operation,
        Dictionary<string, SwaggerSchemaContract>? definitions)
    {
        var schema = operation?.RequestBody?.Content is null
            ? null
            : SelectJsonRequestBodySchema(operation.RequestBody.Content, definitions);
        if (schema is null)
        {
            return;
        }

        operationMap[BuildOperationKey(path, verb)] = schema;
        operationMap[BuildOperationKey(NormalizePath(path), verb)] = schema;
    }

    private static RequestBodySchema? SelectJsonRequestBodySchema(
        Dictionary<string, SwaggerMediaTypeContract> content,
        Dictionary<string, SwaggerSchemaContract>? definitions)
    {
        if (content.Count == 0)
        {
            return null;
        }

        SwaggerSchemaContract? schema = null;
        if (content.TryGetValue("application/json", out var jsonContent))
        {
            schema = jsonContent.Schema;
        }
        else
        {
            schema = content.FirstOrDefault(pair => pair.Key.Contains("json", StringComparison.OrdinalIgnoreCase)).Value?.Schema
                ?? content.Values.FirstOrDefault()?.Schema;
        }

        return schema is null ? null : BuildSchema(schema, definitions, required: true);
    }

    private static RequestBodySchema BuildSchema(SwaggerSchemaContract schema, Dictionary<string, SwaggerSchemaContract>? definitions, bool required)
    {
        if (!string.IsNullOrWhiteSpace(schema.Ref))
        {
            var refName = schema.Ref.Split('/').Last();
            if (definitions?.TryGetValue(refName, out var referenced) == true)
            {
                var resolved = BuildSchema(referenced, definitions, required);
                return resolved with { Required = required || resolved.Required };
            }
        }

        var properties = new Dictionary<string, RequestBodySchema>(StringComparer.OrdinalIgnoreCase);
        if (schema.Properties is not null)
        {
            var requiredFields = schema.Required?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            foreach (var property in schema.Properties)
            {
                properties[property.Key] = BuildSchema(property.Value, definitions, requiredFields.Contains(property.Key));
            }
        }

        RequestBodySchema? items = null;
        if (schema.Items is not null)
        {
            items = BuildSchema(schema.Items, definitions, required: true);
        }

        return new RequestBodySchema(
            schema.Type ?? InferType(schema),
            required,
            properties,
            schema.Required ?? [],
            items,
            schema.Enum?.Select(item => item.Clone()).ToList() ?? [],
            ResolveEnumNames(schema));
    }

    private static string InferType(SwaggerSchemaContract schema)
    {
        if (schema.Properties is not null && schema.Properties.Count > 0)
        {
            return "object";
        }

        if (schema.Items is not null)
        {
            return "array";
        }

        return "string";
    }

    private static List<string> ResolveEnumNames(SwaggerSchemaContract schema)
    {
        if (schema.Extensions is null || schema.Extensions.Count == 0)
        {
            return [];
        }

        if (TryReadStringArray(schema.Extensions, "x-enumNames", out var enumNames))
        {
            return enumNames;
        }

        if (TryReadStringArray(schema.Extensions, "x-enum-varnames", out var enumVarNames))
        {
            return enumVarNames;
        }

        return [];
    }

    private static bool TryReadStringArray(IReadOnlyDictionary<string, JsonElement> extensions, string key, out List<string> values)
    {
        values = [];
        if (!extensions.TryGetValue(key, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        values = element.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
        return values.Count > 0;
    }

    private static string BuildOperationKey(string endpoint, string verb)
        => $"{verb.Trim().ToLowerInvariant()}::{endpoint.Trim()}";

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            if ((segment.StartsWith("{", StringComparison.Ordinal) && segment.EndsWith("}", StringComparison.Ordinal)) ||
                segment.Contains("{{", StringComparison.Ordinal))
            {
                segments[index] = "{}";
            }
        }

        return "/" + string.Join('/', segments);
    }
}

internal sealed record RequestBodySchema(
    string Type,
    bool Required,
    IReadOnlyDictionary<string, RequestBodySchema> Properties,
    IReadOnlyList<string> RequiredFields,
    RequestBodySchema? Items,
    IReadOnlyList<JsonElement> EnumValues,
    IReadOnlyList<string> EnumNames);

internal sealed class SwaggerDocumentContract
{
    public Dictionary<string, SwaggerPathItemContract>? Paths { get; set; }
    public Dictionary<string, SwaggerSchemaContract>? Definitions { get; set; }
    public SwaggerComponentsContract? Components { get; set; }
}

internal sealed class SwaggerComponentsContract
{
    public Dictionary<string, SwaggerSchemaContract>? Schemas { get; set; }
}

internal sealed class SwaggerPathItemContract
{
    public SwaggerOperationContract? Get { get; set; }
    public SwaggerOperationContract? Post { get; set; }
    public SwaggerOperationContract? Put { get; set; }
    public SwaggerOperationContract? Patch { get; set; }
    public SwaggerOperationContract? Delete { get; set; }
}

internal sealed class SwaggerOperationContract
{
    public SwaggerRequestBodyContract? RequestBody { get; set; }
}

internal sealed class SwaggerRequestBodyContract
{
    public Dictionary<string, SwaggerMediaTypeContract>? Content { get; set; }
}

internal sealed class SwaggerMediaTypeContract
{
    public SwaggerSchemaContract? Schema { get; set; }
}

internal sealed class SwaggerSchemaContract
{
    public string? Type { get; set; }
    public string? Format { get; set; }
    public string? Ref { get; set; }
    public Dictionary<string, SwaggerSchemaContract>? Properties { get; set; }
    public List<string>? Required { get; set; }
    public List<JsonElement>? Enum { get; set; }
    public SwaggerSchemaContract? Items { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }

    [JsonPropertyName("$ref")]
    public string? RefProperty
    {
        get => Ref;
        set => Ref = value;
    }
}
