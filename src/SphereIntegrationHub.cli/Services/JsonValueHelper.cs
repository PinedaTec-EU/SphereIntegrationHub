using System.Globalization;
using System.Text.Json;

namespace SphereIntegrationHub.Services;

internal static class JsonValueHelper
{
    private static readonly JsonElement NullElement = JsonDocument.Parse("null").RootElement.Clone();

    public static bool TryParse(string? raw, out JsonElement element)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            element = default;
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            element = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            element = default;
            return false;
        }
    }

    public static string ToDisplayString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            _ => element.GetRawText()
        };
    }

    public static bool TryResolvePath(JsonElement element, IReadOnlyList<string> segments, out JsonElement resolved)
    {
        resolved = element;
        foreach (var segment in segments)
        {
            var isOptional = segment.EndsWith("?", StringComparison.Ordinal);
            var normalizedSegment = isOptional ? segment[..^1] : segment;

            if (resolved.ValueKind == JsonValueKind.Object && resolved.TryGetProperty(normalizedSegment, out var property))
            {
                resolved = property;
                continue;
            }

            if (resolved.ValueKind == JsonValueKind.Array &&
                int.TryParse(normalizedSegment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) &&
                index >= 0 &&
                index < resolved.GetArrayLength())
            {
                resolved = resolved[index];
                continue;
            }

            if (isOptional)
            {
                resolved = NullElement;
                return true;
            }

            resolved = default;
            return false;
        }

        resolved = resolved.Clone();
        return true;
    }

    public static bool IsEmpty(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Array => element.GetArrayLength() == 0,
            JsonValueKind.Object => !element.EnumerateObject().Any(),
            JsonValueKind.String => string.IsNullOrEmpty(element.GetString()),
            JsonValueKind.Null => true,
            JsonValueKind.Undefined => true,
            _ => false
        };
    }

    public static int GetLength(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Array => element.GetArrayLength(),
            JsonValueKind.Object => element.EnumerateObject().Count(),
            JsonValueKind.String => element.GetString()?.Length ?? 0,
            JsonValueKind.Null => 0,
            JsonValueKind.Undefined => 0,
            _ => 1
        };
    }

    public static bool TryGetFirst(JsonElement element, out JsonElement first)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array when element.GetArrayLength() > 0:
                first = element[0].Clone();
                return true;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    first = property.Value.Clone();
                    return true;
                }
                break;
        }

        first = default;
        return false;
    }

    public static bool Any(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Array => element.GetArrayLength() > 0,
            JsonValueKind.Object => element.EnumerateObject().Any(),
            JsonValueKind.String => !string.IsNullOrEmpty(element.GetString()),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => false,
            JsonValueKind.Undefined => false,
            _ => true
        };
    }
}
