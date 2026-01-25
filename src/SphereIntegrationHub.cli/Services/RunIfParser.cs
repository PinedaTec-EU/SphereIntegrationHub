using System.Text.RegularExpressions;

namespace SphereIntegrationHub.Services;

public static class RunIfParser
{
    private static readonly Regex ExpressionRegex = new(
         @"^\s*\{\{\s*(.+?)\s*\}\}\s*(==|!=|in|not\s+in)\s*(null|""[^""]*""|'[^']*'|-?\d+(?:\.\d+)?|\[(?:\s*-?\d+(?:\.\d+)?\s*(?:,\s*-?\d+(?:\.\d+)?\s*)*)?\])\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool TryParse(string expression, out string token, out string op, out string rawValue)
    {
        using var activity = Telemetry.ActivitySource.StartActivity(TelemetryConstants.ActivityRunIfParse);
        activity?.SetTag(TelemetryConstants.TagExpressionLength, expression?.Length ?? 0);

        token = string.Empty;
        op = string.Empty;
        rawValue = string.Empty;

        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        var match = ExpressionRegex.Match(expression);
        if (!match.Success)
        {
            return false;
        }

        token = match.Groups[1].Value;
        op = Regex.Replace(match.Groups[2].Value, @"\s+", " ").Trim().ToLowerInvariant();
        rawValue = match.Groups[3].Value;
        return true;
    }
}
