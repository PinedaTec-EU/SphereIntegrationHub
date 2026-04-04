using System.Collections;
using System.Globalization;
using System.Text.Json;

namespace SphereIntegrationHub.Services;

internal static class WorkflowStageInputValueHelper
{
    public static string ResolveToInputString(object? value, TemplateResolver templateResolver, TemplateContext templateContext)
    {
        var resolvedValue = ResolveTemplates(value, templateResolver, templateContext);
        return ToInputString(resolvedValue);
    }

    public static string ToDisplayString(object? value)
    {
        return value switch
        {
            null => "null",
            string text => text,
            bool boolean => boolean ? "true" : "false",
            _ when IsNumeric(value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => JsonSerializer.Serialize(NormalizeStructuredValue(value))
        };
    }

    public static void ValidateTemplates(
        object? value,
        Action<string> validateTemplate)
    {
        switch (value)
        {
            case null:
                return;
            case string text:
                validateTemplate(text);
                return;
            case IDictionary dictionary:
                foreach (DictionaryEntry entry in dictionary)
                {
                    ValidateTemplates(entry.Value, validateTemplate);
                }
                return;
            case IEnumerable enumerable when value is not string:
                foreach (var item in enumerable)
                {
                    ValidateTemplates(item, validateTemplate);
                }
                return;
            default:
                return;
        }
    }

    private static object? ResolveTemplates(object? value, TemplateResolver templateResolver, TemplateContext templateContext)
    {
        switch (value)
        {
            case null:
                return null;
            case string text:
                return templateResolver.ResolveTemplate(text, templateContext);
            case IDictionary dictionary:
                var objectResult = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (DictionaryEntry entry in dictionary)
                {
                    var key = Convert.ToString(entry.Key, CultureInfo.InvariantCulture);
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    objectResult[key] = ResolveTemplates(entry.Value, templateResolver, templateContext);
                }

                return objectResult;
            case IEnumerable enumerable when value is not string:
                var listResult = new List<object?>();
                foreach (var item in enumerable)
                {
                    listResult.Add(ResolveTemplates(item, templateResolver, templateContext));
                }

                return listResult;
            default:
                return value;
        }
    }

    private static string ToInputString(object? value)
    {
        return value switch
        {
            null => "null",
            string text => text,
            bool boolean => boolean ? "true" : "false",
            _ when IsNumeric(value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => JsonSerializer.Serialize(NormalizeStructuredValue(value))
        };
    }

    private static object? NormalizeStructuredValue(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case IDictionary dictionary:
                var normalizedObject = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (DictionaryEntry entry in dictionary)
                {
                    var key = Convert.ToString(entry.Key, CultureInfo.InvariantCulture);
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    normalizedObject[key] = NormalizeStructuredValue(entry.Value);
                }

                return normalizedObject;
            case IEnumerable enumerable when value is not string:
                var normalizedList = new List<object?>();
                foreach (var item in enumerable)
                {
                    normalizedList.Add(NormalizeStructuredValue(item));
                }

                return normalizedList;
            default:
                return value;
        }
    }

    private static bool IsNumeric(object? value)
    {
        return value is sbyte or byte or short or ushort or int or uint or long or ulong or
            float or double or decimal;
    }
}
