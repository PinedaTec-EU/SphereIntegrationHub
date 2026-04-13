using System.Text.Json;

using SphereIntegrationHub.cli;

namespace SphereIntegrationHub.Services;

public enum ExecutionReportFormat
{
    None,
    Json,
    Html,
    Both
}

public enum ExecutionHttpCaptureMode
{
    None,
    Headers,
    Bodies
}

public sealed record WorkflowExecutionReportOptions(
    bool Enabled,
    ExecutionReportFormat Format,
    ExecutionHttpCaptureMode CaptureHttp,
    bool RedactSensitiveData,
    bool SummaryConsole)
{
    public static WorkflowExecutionReportOptions Default { get; } =
        new(true, ExecutionReportFormat.Json, ExecutionHttpCaptureMode.Headers, true, true);
}

public sealed class WorkflowExecutionReport
{
    public string ExecutionId { get; init; } = Ulid.NewUlid().ToString();
    public string WorkflowName { get; init; } = string.Empty;
    public string WorkflowId { get; init; } = string.Empty;
    public string WorkflowVersion { get; init; } = string.Empty;
    // Version of the SIH tool that produced this report. Empty for reports generated before this field was introduced.
    public string ToolVersion { get; init; } = string.Empty;
    public string WorkflowPath { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset? FinishedAtUtc { get; set; }
    public long DurationMs { get; set; }
    public string Result { get; set; } = "Running";
    public string? ErrorMessage { get; set; }
    public bool Mocked { get; init; }
    public bool DryRun { get; init; }
    public Dictionary<string, string> Inputs { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, object?> Output { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public WorkflowPreflightReport Preflight { get; init; } = new();
    public List<WorkflowStageExecutionRecord> Stages { get; } = [];
    public WorkflowExecutionMetrics Metrics { get; } = new();
    public string? OutputFilePath { get; set; }
}

public sealed class WorkflowExecutionMetrics
{
    public int TotalStages { get; set; }
    public int ExecutedStages { get; set; }
    public int SkippedStages { get; set; }
    public int FailedStages { get; set; }
    public int MockedStages { get; set; }
    public int HttpStages { get; set; }
    public int WorkflowStages { get; set; }
    public int JumpedStages { get; set; }
    public int TotalRetries { get; set; }
    public int PreflightRetries { get; set; }
}

public sealed class WorkflowPreflightReport
{
    public List<WorkflowPreflightOperationRecord> Operations { get; } = [];
    public int TotalRetries { get; set; }
    public long TotalDelayMs { get; set; }
    public long DurationMs { get; set; }
}

public sealed class WorkflowPreflightOperationRecord
{
    public string OperationType { get; set; } = string.Empty;
    public string DefinitionName { get; set; } = string.Empty;
    public string? Target { get; set; }
    public string Status { get; set; } = "Pending";
    public string? Message { get; set; }
    public int RetryCount { get; set; }
    public long DurationMs { get; set; }
    public List<WorkflowPreflightAttemptRecord> Attempts { get; } = [];
}

public sealed class WorkflowPreflightAttemptRecord
{
    public int AttemptNumber { get; set; }
    public string? RequestUri { get; set; }
    public string Status { get; set; } = "Pending";
    public int? HttpStatusCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? FinishedAtUtc { get; set; }
    public long DurationMs { get; set; }
}

public sealed class WorkflowStageExecutionRecord
{
    public string WorkflowName { get; init; } = string.Empty;
    public string StageName { get; init; } = string.Empty;
    public string StageKind { get; init; } = string.Empty;
    public int Depth { get; init; }
    public string Status { get; set; } = "Running";
    public string? RunIf { get; init; }
    public string? JumpTarget { get; set; }
    public bool Mocked { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset? FinishedAtUtc { get; set; }
    public long DurationMs { get; set; }
    public int? DelaySeconds { get; set; }
    public string? ForEachExecutionMode { get; set; }
    public int RetryCount { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? EnsureMode { get; set; }
    public string? EnsureStatus { get; set; }
    public string? RequestUri { get; set; }
    public string? HttpMethod { get; set; }
    public Dictionary<string, string>? RequestHeaders { get; set; }
    public string? RequestBody { get; set; }
    public Dictionary<string, string>? ResponseHeaders { get; set; }
    public string? ResponseBody { get; set; }
    public Dictionary<string, object?> Output { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, object?> WorkflowInputs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, object?> WorkflowOutput { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, object?> WorkflowResult { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record WorkflowExecutionArtifacts(
    string? JsonReportPath,
    string? HtmlReportPath);

internal static class WorkflowExecutionReportOptionsResolver
{
    public static WorkflowExecutionReportOptions Resolve(ReportingConfig config, InlineArguments arguments)
    {
        var enabled = !string.Equals(arguments.ReportFormat, "none", StringComparison.OrdinalIgnoreCase) && config.Enabled;
        var format = ParseFormat(arguments.ReportFormat) ?? ParseFormat(config.Format) ?? ExecutionReportFormat.Json;
        if (string.Equals(arguments.ReportFormat, "none", StringComparison.OrdinalIgnoreCase))
        {
            format = ExecutionReportFormat.None;
        }

        var capture = ParseCaptureMode(arguments.CaptureHttp) ?? ParseCaptureMode(config.CaptureHttp) ?? ExecutionHttpCaptureMode.Headers;
        var redact = arguments.RedactSensitiveData ?? config.RedactSensitiveData;
        var summary = arguments.SummaryConsole ?? config.SummaryConsole;
        return new WorkflowExecutionReportOptions(enabled && format != ExecutionReportFormat.None, format, capture, redact, summary);
    }

    public static ExecutionReportFormat? ParseFormat(string? raw)
    {
        return raw?.Trim().ToLowerInvariant() switch
        {
            null or "" => null,
            "none" => ExecutionReportFormat.None,
            "json" => ExecutionReportFormat.Json,
            "html" => ExecutionReportFormat.Html,
            "both" => ExecutionReportFormat.Both,
            _ => throw new InvalidOperationException($"Unknown report format '{raw}'. Use json, html, both, or none.")
        };
    }

    public static ExecutionHttpCaptureMode? ParseCaptureMode(string? raw)
    {
        return raw?.Trim().ToLowerInvariant() switch
        {
            null or "" => null,
            "none" => ExecutionHttpCaptureMode.None,
            "headers" => ExecutionHttpCaptureMode.Headers,
            "bodies" => ExecutionHttpCaptureMode.Bodies,
            _ => throw new InvalidOperationException($"Unknown capture-http value '{raw}'. Use none, headers, or bodies.")
        };
    }
}

internal static class WorkflowExecutionRedactor
{
    private const string MaskedValue = "*****";
    private static readonly string[] SensitiveKeys = ["authorization", "cookie", "set-cookie", "token", "secret", "password", "apikey", "api-key"];

    public static Dictionary<string, string>? RedactHeaders(IReadOnlyDictionary<string, string>? headers, bool enabled)
    {
        if (headers is null)
        {
            return null;
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in headers)
        {
            result[pair.Key] = enabled && IsSensitive(pair.Key) ? "***REDACTED***" : pair.Value;
        }

        return result;
    }

    public static string? RedactBody(string? body, bool enabled)
    {
        if (!enabled || string.IsNullOrWhiteSpace(body))
        {
            return body;
        }

        if (!JsonValueHelper.TryParse(body, out var json))
        {
            return "***REDACTED***";
        }

        return RedactJsonElement(json).GetRawText();
    }

    public static Dictionary<string, object?> ConvertOutputs(
        IReadOnlyDictionary<string, string> outputs,
        IReadOnlySet<string>? secretKeys = null,
        IReadOnlySet<string>? secretValues = null)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in RedactOutputStrings(outputs, secretKeys, secretValues))
        {
            if (JsonValueHelper.TryParse(pair.Value, out var json))
            {
                result[pair.Key] = json;
            }
            else
            {
                result[pair.Key] = pair.Value;
            }
        }

        return result;
    }

    public static Dictionary<string, string> RedactOutputStrings(
        IReadOnlyDictionary<string, string> outputs,
        IReadOnlySet<string>? secretKeys = null,
        IReadOnlySet<string>? secretValues = null)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in outputs)
        {
            result[pair.Key] = RedactOutputValue(pair.Key, pair.Value, secretKeys, secretValues);
        }

        return result;
    }

    public static string RedactOutputValue(
        string key,
        string value,
        IReadOnlySet<string>? secretKeys = null,
        IReadOnlySet<string>? secretValues = null)
    {
        if (secretKeys?.Contains(key) == true)
        {
            return MaskedValue;
        }

        return ReplaceSecretValues(value, secretValues);
    }

    private static bool IsSensitive(string key)
        => SensitiveKeys.Any(fragment => key.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static string ReplaceSecretValues(string value, IReadOnlySet<string>? secretValues)
    {
        if (string.IsNullOrEmpty(value) || secretValues is null || secretValues.Count == 0)
        {
            return value;
        }

        var redacted = value;
        foreach (var secretValue in secretValues
                     .Where(static value => !string.IsNullOrEmpty(value))
                     .Distinct(StringComparer.Ordinal)
                     .OrderByDescending(static value => value.Length))
        {
            redacted = redacted.Replace(secretValue, MaskedValue, StringComparison.Ordinal);
        }

        return redacted;
    }

    private static JsonElement RedactJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => JsonSerializer.SerializeToElement(
                element.EnumerateObject().ToDictionary(
                    property => property.Name,
                    property => IsSensitive(property.Name)
                        ? "***REDACTED***"
                        : JsonSerializer.Deserialize<object?>(RedactJsonElement(property.Value).GetRawText()))),
            JsonValueKind.Array => JsonSerializer.SerializeToElement(
                element.EnumerateArray()
                    .Select(item => JsonSerializer.Deserialize<object?>(RedactJsonElement(item).GetRawText()))
                    .ToArray()),
            _ => element.Clone()
        };
    }
}
