using SphereIntegrationHub.MCP.Models;
using System.Text.Json;

namespace SphereIntegrationHub.MCP.Tools;

internal static class ToolArgumentParser
{
    public static bool TryReadBool(
        Dictionary<string, object>? arguments, string key, bool defaultValue = false)
    {
        if (arguments?.TryGetValue(key, out var obj) != true)
            return defaultValue;

        if (obj is bool boolValue)
            return boolValue;

        return obj.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
    }

    internal static EndpointInfo? TryParseEndpointSchema(
        Dictionary<string, object>? arguments, bool required)
    {
        if (arguments?.TryGetValue("endpointSchema", out var endpointSchemaObj) != true)
        {
            if (required)
                throw new ArgumentException("endpointSchema is required");
            return null;
        }

        JsonElement endpointJson;
        if (endpointSchemaObj is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            endpointJson = element;
        }
        else
        {
            throw new ArgumentException("endpointSchema must be a JSON object");
        }

        var apiName = endpointJson.TryGetProperty("apiName", out var apiEl)
            ? apiEl.GetString() : null;
        var endpoint = endpointJson.TryGetProperty("endpoint", out var endpointEl)
            ? endpointEl.GetString() : null;
        var httpVerb = endpointJson.TryGetProperty("httpVerb", out var verbEl)
            ? verbEl.GetString() : null;

        if (string.IsNullOrWhiteSpace(apiName) ||
            string.IsNullOrWhiteSpace(endpoint) ||
            string.IsNullOrWhiteSpace(httpVerb))
        {
            throw new ArgumentException("endpointSchema must include apiName, endpoint and httpVerb");
        }

        return new EndpointInfo
        {
            ApiName = apiName!,
            Endpoint = endpoint!,
            HttpVerb = httpVerb!.ToUpperInvariant(),
            Summary = endpointJson.TryGetProperty("summary", out var summaryEl)
                ? summaryEl.GetString() ?? string.Empty : string.Empty,
            Description = endpointJson.TryGetProperty("description", out var descriptionEl)
                ? descriptionEl.GetString() ?? string.Empty : string.Empty,
            QueryParameters = ParseParameters(endpointJson, "queryParameters"),
            HeaderParameters = ParseParameters(endpointJson, "headerParameters"),
            PathParameters = ParseParameters(endpointJson, "pathParameters"),
            BodySchema = ParseBodySchema(endpointJson),
            Responses = ParseResponses(endpointJson),
            Tags = endpointJson.TryGetProperty("tags", out var tagsEl) && tagsEl.ValueKind == JsonValueKind.Array
                ? tagsEl.EnumerateArray()
                    .Select(x => x.GetString() ?? string.Empty)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList()
                : []
        };
    }

    private static List<ParameterInfo> ParseParameters(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var arrayEl) || arrayEl.ValueKind != JsonValueKind.Array)
            return [];

        var parameters = new List<ParameterInfo>();
        foreach (var item in arrayEl.EnumerateArray())
        {
            var name = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var type = item.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : "string";
            var required = item.TryGetProperty("required", out var reqEl) &&
                reqEl.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                reqEl.GetBoolean();

            parameters.Add(new ParameterInfo
            {
                Name = name!,
                Type = string.IsNullOrWhiteSpace(type) ? "string" : type!,
                Required = required,
                Description = item.TryGetProperty("description", out var descEl) ? descEl.GetString() : null,
                DefaultValue = item.TryGetProperty("defaultValue", out var defaultEl) ? defaultEl.ToString() : null
            });
        }

        return parameters;
    }

    private static BodySchema? ParseBodySchema(JsonElement root)
    {
        if (!root.TryGetProperty("bodySchema", out var bodyEl) || bodyEl.ValueKind != JsonValueKind.Object)
            return null;

        if (!bodyEl.TryGetProperty("fields", out var fieldsEl) || fieldsEl.ValueKind != JsonValueKind.Object)
            return null;

        var fields = new Dictionary<string, FieldSchema>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in fieldsEl.EnumerateObject())
        {
            var fieldValue = property.Value;
            var type = fieldValue.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : "string";

            fields[property.Name] = new FieldSchema
            {
                Type = string.IsNullOrWhiteSpace(type) ? "string" : type!,
                Format = fieldValue.TryGetProperty("format", out var formatEl) ? formatEl.GetString() : null,
                Description = fieldValue.TryGetProperty("description", out var descriptionEl)
                    ? descriptionEl.GetString() : null,
                IsArray = fieldValue.TryGetProperty("isArray", out var isArrayEl) &&
                    isArrayEl.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                    isArrayEl.GetBoolean(),
                EnumValues = fieldValue.TryGetProperty("enumValues", out var enumEl) &&
                    enumEl.ValueKind == JsonValueKind.Array
                    ? enumEl.EnumerateArray()
                        .Select(x => x.GetString() ?? string.Empty)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToList()
                    : null
            };
        }

        var requiredFields = bodyEl.TryGetProperty("requiredFields", out var requiredEl) &&
            requiredEl.ValueKind == JsonValueKind.Array
            ? requiredEl.EnumerateArray()
                .Select(x => x.GetString() ?? string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList()
            : (List<string>)[];

        return new BodySchema { Fields = fields, RequiredFields = requiredFields };
    }

    private static Dictionary<int, ResponseSchema> ParseResponses(JsonElement root)
    {
        if (!root.TryGetProperty("responses", out var responsesEl) || responsesEl.ValueKind != JsonValueKind.Array)
            return [];

        var responses = new Dictionary<int, ResponseSchema>();
        foreach (var responseEl in responsesEl.EnumerateArray())
        {
            if (!responseEl.TryGetProperty("statusCode", out var statusCodeEl) ||
                !statusCodeEl.TryGetInt32(out var statusCode))
                continue;

            responses[statusCode] = new ResponseSchema
            {
                StatusCode = statusCode,
                Description = responseEl.TryGetProperty("description", out var descEl)
                    ? descEl.GetString() ?? string.Empty : string.Empty,
                Fields = null
            };
        }

        return responses;
    }
}
