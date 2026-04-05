using System.Text.RegularExpressions;

namespace SphereIntegrationHub.cli;

internal static partial class ConsoleMessageFormatter
{
    private const string ResetCode = "\u001b[0m";
    private const string GreenCode = "\u001b[32m";
    private const string RedCode = "\u001b[31m";

    public static string FormatInfo(string message, bool enabled)
    {
        if (!enabled || string.IsNullOrEmpty(message))
        {
            return message;
        }

        var keyValueMatch = KeyValuePattern().Match(message);
        if (keyValueMatch.Success)
        {
            var prefix = keyValueMatch.Groups["prefix"].Value;
            var value = keyValueMatch.Groups["value"].Value;
            if (TryResolveSuccessColor(value, out var successColor))
            {
                return prefix + Colorize(value, successColor);
            }

            if (TryResolveFailureColor(value, out var failureColor))
            {
                return prefix + Colorize(value, failureColor);
            }

            return message;
        }

        var leadingStatusMatch = LeadingStatusPattern().Match(message);
        if (!leadingStatusMatch.Success)
        {
            return message;
        }

        var status = leadingStatusMatch.Groups["status"].Value;
        var remainder = leadingStatusMatch.Groups["rest"].Value;
        if (TryResolveSuccessColor(status, out var leadingSuccessColor))
        {
            return Colorize(status, leadingSuccessColor) + remainder;
        }

        if (TryResolveFailureColor(status, out var leadingFailureColor))
        {
            return Colorize(status, leadingFailureColor) + remainder;
        }

        return message;
    }

    public static string FormatError(string message, bool enabled)
    {
        if (!enabled || string.IsNullOrEmpty(message))
        {
            return message;
        }

        return Colorize(message, RedCode);
    }

    private static bool TryResolveSuccessColor(string value, out string colorCode)
    {
        var normalized = value.Trim();
        if (normalized.Equals("ok", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("success", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("passed", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("created", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("existing", StringComparison.OrdinalIgnoreCase))
        {
            colorCode = GreenCode;
            return true;
        }

        colorCode = string.Empty;
        return false;
    }

    private static bool TryResolveFailureColor(string value, out string colorCode)
    {
        var normalized = value.Trim();
        if (normalized.Equals("error", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("failed", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("failure", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("ko", StringComparison.OrdinalIgnoreCase))
        {
            colorCode = RedCode;
            return true;
        }

        colorCode = string.Empty;
        return false;
    }

    private static string Colorize(string value, string colorCode)
        => $"{colorCode}{value}{ResetCode}";

    [GeneratedRegex("^(?<prefix>\\s*[^:]+:\\s*)(?<value>.+)$", RegexOptions.Compiled)]
    private static partial Regex KeyValuePattern();

    [GeneratedRegex("^(?<status>OK|Ok|SUCCESS|Success|PASSED|Passed|ERROR|Error|FAILED|Failed|FAILURE|Failure|KO|Ko)(?<rest>\\b.*)$", RegexOptions.Compiled)]
    private static partial Regex LeadingStatusPattern();
}
