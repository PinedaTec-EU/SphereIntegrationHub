using System.Globalization;

namespace SphereIntegrationHub.Services.Validation;

internal static class WorkflowRandomTokenValidator
{
    public static void Validate(
        string token,
        string location,
        List<string> errors,
        Action<string> validateReference)
    {
        var openIndex = token.IndexOf('(');
        var closeIndex = token.LastIndexOf(')');
        if (openIndex <= "rand:".Length || closeIndex != token.Length - 1)
        {
            errors.Add($"Invalid rand token '{token}' in {location}.");
            return;
        }

        var functionName = token["rand:".Length..openIndex].Trim().ToLowerInvariant();
        var arguments = SplitFunctionArguments(token[(openIndex + 1)..closeIndex])
            .Select(argument => argument.Trim())
            .Where(argument => argument.Length > 0)
            .ToArray();

        bool valid = functionName switch
        {
            "number" => ValidateNumberToken(token, arguments, location, errors),
            "text" => ValidateTextToken(token, arguments, location, errors),
            "guid" => ValidateArgumentCount(token, arguments.Length, 0, 0, location, errors, "rand:guid expects no arguments."),
            "ulid" => ValidateArgumentCount(token, arguments.Length, 0, 0, location, errors, "rand:ulid expects no arguments."),
            "date" => ValidateDateToken(token, arguments, location, errors),
            "datetime" => ValidateDateTimeToken(token, arguments, location, errors),
            "time" => ValidateTimeToken(token, arguments, location, errors),
            _ => AddFunctionError(token, location, errors)
        };

        if (!valid)
        {
            return;
        }

        foreach (var argument in arguments.Where(LooksLikeTokenReference))
        {
            validateReference(argument);
        }
    }

    private static bool ValidateArgumentCount(
        string token,
        int count,
        int min,
        int max,
        string location,
        List<string> errors,
        string? detail = null)
    {
        if (count >= min && count <= max)
        {
            return true;
        }

        errors.Add(detail is null
            ? $"Invalid rand token '{token}' in {location}."
            : $"Invalid rand token '{token}' in {location}: {detail}");
        return false;
    }

    private static bool AddFunctionError(string token, string location, List<string> errors)
    {
        errors.Add($"Unknown rand token '{token}' in {location}.");
        return false;
    }

    private static bool ValidateNumberToken(string token, string[] arguments, string location, List<string> errors)
    {
        if (!ValidateArgumentCount(token, arguments.Length, 0, 2, location, errors, "rand:number expects 0, 1 or 2 integer arguments."))
        {
            return false;
        }

        return ValidateIntegerArgument(arguments, 0, token, location, errors, "minimum") &&
               ValidateIntegerArgument(arguments, 1, token, location, errors, "maximum");
    }

    private static bool ValidateTextToken(string token, string[] arguments, string location, List<string> errors)
    {
        if (!ValidateArgumentCount(token, arguments.Length, 0, 2, location, errors, "rand:text expects length and optional character set."))
        {
            return false;
        }

        if (!ValidateIntegerArgument(arguments, 0, token, location, errors, "length"))
        {
            return false;
        }

        if (arguments.Length < 2 || LooksLikeTokenReference(arguments[1]))
        {
            return true;
        }

        var characterSet = NormalizeLiteral(arguments[1]);
        if (IsSupportedCharacterSet(characterSet))
        {
            return true;
        }

        errors.Add($"Invalid rand token '{token}' in {location}: unsupported character set '{characterSet}'. Supported values: alpha, alpha-lower, alpha-upper, alnum, numeric, ascii.");
        return false;
    }

    private static bool ValidateDateToken(string token, string[] arguments, string location, List<string> errors)
    {
        if (!ValidateArgumentCount(token, arguments.Length, 0, 3, location, errors, "rand:date expects up to 3 arguments: from, to, format."))
        {
            return false;
        }

        return ValidateDateArgument(arguments, 0, token, location, errors, "from") &&
               ValidateDateArgument(arguments, 1, token, location, errors, "to");
    }

    private static bool ValidateDateTimeToken(string token, string[] arguments, string location, List<string> errors)
    {
        if (!ValidateArgumentCount(token, arguments.Length, 0, 3, location, errors, "rand:datetime expects up to 3 arguments: from, to, format."))
        {
            return false;
        }

        return ValidateDateTimeArgument(arguments, 0, token, location, errors, "from") &&
               ValidateDateTimeArgument(arguments, 1, token, location, errors, "to");
    }

    private static bool ValidateTimeToken(string token, string[] arguments, string location, List<string> errors)
    {
        if (!ValidateArgumentCount(token, arguments.Length, 0, 3, location, errors, "rand:time expects up to 3 arguments: from, to, format."))
        {
            return false;
        }

        return ValidateTimeArgument(arguments, 0, token, location, errors, "from") &&
               ValidateTimeArgument(arguments, 1, token, location, errors, "to");
    }

    private static bool ValidateIntegerArgument(string[] arguments, int index, string token, string location, List<string> errors, string label)
    {
        if (arguments.Length <= index || LooksLikeTokenReference(arguments[index]))
        {
            return true;
        }

        var value = NormalizeLiteral(arguments[index]);
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return true;
        }

        errors.Add($"Invalid rand token '{token}' in {location}: {label} '{value}' is not a valid integer.");
        return false;
    }

    private static bool ValidateDateArgument(string[] arguments, int index, string token, string location, List<string> errors, string label)
    {
        if (arguments.Length <= index || LooksLikeTokenReference(arguments[index]))
        {
            return true;
        }

        var value = NormalizeLiteral(arguments[index]);
        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            return true;
        }

        errors.Add($"Invalid rand token '{token}' in {location}: {label} '{value}' is not a valid date.");
        return false;
    }

    private static bool ValidateDateTimeArgument(string[] arguments, int index, string token, string location, List<string> errors, string label)
    {
        if (arguments.Length <= index || LooksLikeTokenReference(arguments[index]))
        {
            return true;
        }

        var value = NormalizeLiteral(arguments[index]);
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
        {
            return true;
        }

        errors.Add($"Invalid rand token '{token}' in {location}: {label} '{value}' is not a valid datetime.");
        return false;
    }

    private static bool ValidateTimeArgument(string[] arguments, int index, string token, string location, List<string> errors, string label)
    {
        if (arguments.Length <= index || LooksLikeTokenReference(arguments[index]))
        {
            return true;
        }

        var value = NormalizeLiteral(arguments[index]);
        if (TimeOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            return true;
        }

        errors.Add($"Invalid rand token '{token}' in {location}: {label} '{value}' is not a valid time.");
        return false;
    }

    private static string NormalizeLiteral(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '\'' && trimmed[^1] == '\'') ||
             (trimmed[0] == '"' && trimmed[^1] == '"')))
        {
            return trimmed[1..^1];
        }

        return trimmed;
    }

    private static bool IsSupportedCharacterSet(string value)
    {
        return value.Equals("alpha", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("alpha-lower", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("alpha-upper", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("alnum", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("numeric", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("ascii", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeTokenReference(string value)
    {
        return value.StartsWith("input.", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("global.", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("context:", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("context.", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("endpoint:", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("workflow:", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("stage:", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("var:", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("env:", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("system:", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("coalesce(", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> SplitFunctionArguments(string content)
    {
        var depth = 0;
        char? quote = null;
        var start = 0;

        for (var i = 0; i < content.Length; i++)
        {
            if (quote.HasValue)
            {
                if (content[i] == quote.Value && (i == 0 || content[i - 1] != '\\'))
                {
                    quote = null;
                }

                continue;
            }

            switch (content[i])
            {
                case '\'':
                case '"':
                    quote = content[i];
                    break;
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
                case ',' when depth == 0:
                    yield return content[start..i];
                    start = i + 1;
                    break;
            }
        }

        yield return content[start..];
    }
}
