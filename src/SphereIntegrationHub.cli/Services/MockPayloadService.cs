using System.Text.Json;
using System.Text.RegularExpressions;

namespace SphereIntegrationHub.Services;

public sealed class MockPayloadService
{
    public string LoadRawPayload(string payload, string workflowPath)
    {
        using var activity = Telemetry.ActivitySource.StartActivity(TelemetryConstants.ActivityMockPayloadLoad);

        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new InvalidOperationException("Mock payload is required.");
        }

        return payload;
    }

    public string LoadRawPayloadFromFile(string payloadFile, string workflowPath)
    {
        using var activity = Telemetry.ActivitySource.StartActivity(TelemetryConstants.ActivityMockPayloadLoadFromFile);
        activity?.SetTag(TelemetryConstants.TagFilePath, payloadFile);

        if (string.IsNullOrWhiteSpace(payloadFile))
        {
            throw new InvalidOperationException("Mock payload file is required.");
        }

        var baseDirectory = Path.GetDirectoryName(workflowPath) ?? string.Empty;
        var resolvedPath = Path.IsPathRooted(payloadFile)
            ? payloadFile
            : Path.GetFullPath(Path.Combine(baseDirectory, payloadFile));

        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException("Mock payload file was not found.", resolvedPath);
        }

        return File.ReadAllText(resolvedPath);
    }

    public static string SanitizeJsonForValidation(string json)
    {
        using var activity = Telemetry.ActivitySource.StartActivity(TelemetryConstants.ActivityMockPayloadValidate);
        var sanitized = Regex.Replace(json, "\"\\s*\\{\\{.+?\\}\\}\\s*\"", "\"__token__\"");
        return Regex.Replace(sanitized, "\\{\\{.+?\\}\\}", "0");
    }

    public static bool TryParseJson(string json, out string? error)
    {
        using var activity = Telemetry.ActivitySource.StartActivity(TelemetryConstants.ActivityMockPayloadValidate);
        try
        {
            using var _ = JsonDocument.Parse(json);
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

}
