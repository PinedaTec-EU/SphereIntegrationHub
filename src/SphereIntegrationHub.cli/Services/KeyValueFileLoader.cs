namespace SphereIntegrationHub.Services;

internal static class KeyValueFileLoader
{
    public static IReadOnlyDictionary<string, string> Load(
        string filePath,
        char separator,
        bool allowExportPrefix,
        string invalidEntryMessage)
    {
        using var activity = Telemetry.ActivitySource.StartActivity(TelemetryConstants.ActivityKeyValueLoad);
        activity?.SetTag(TelemetryConstants.TagFilePath, filePath);
        activity?.SetTag(TelemetryConstants.TagFileSeparator, separator.ToString());
        activity?.SetTag(TelemetryConstants.TagFileAllowExport, allowExportPrefix);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = File.ReadAllLines(filePath);
        for (var lineNumber = 0; lineNumber < lines.Length; lineNumber++)
        {
            var line = lines[lineNumber].Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            if (allowExportPrefix && line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
            {
                line = line[7..].TrimStart();
            }

            var separatorIndex = line.IndexOf(separator, StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                throw new InvalidOperationException(string.Format(invalidEntryMessage, lineNumber + 1));
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException(string.Format(invalidEntryMessage, lineNumber + 1));
            }

            values[key] = Unquote(value);
        }

        return values;
    }

    private static string Unquote(string value)
    {
        if (value.Length < 2)
        {
            return value;
        }

        if ((value.StartsWith('"') && value.EndsWith('"')) ||
            (value.StartsWith('\'') && value.EndsWith('\'')))
        {
            return value[1..^1];
        }

        return value;
    }
}
