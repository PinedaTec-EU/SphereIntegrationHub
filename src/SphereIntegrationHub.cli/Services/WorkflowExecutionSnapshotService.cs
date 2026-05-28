using System.Text.Json;

namespace SphereIntegrationHub.Services;

public sealed class WorkflowExecutionSnapshotService
{
    private const int MaxDifferences = 50;
    private const string SnapshotSchemaVersion = "1.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<WorkflowSnapshotCreateResult> CreateAsync(
        string executionReportPath,
        string? snapshotPath,
        string? snapshotName,
        CancellationToken cancellationToken)
    {
        var report = await LoadReportAsync(executionReportPath, cancellationToken);
        var name = string.IsNullOrWhiteSpace(snapshotName)
            ? report.WorkflowVersion
            : snapshotName.Trim();
        var outputPath = string.IsNullOrWhiteSpace(snapshotPath)
            ? BuildDefaultSnapshotPath(executionReportPath, report, name)
            : Path.GetFullPath(snapshotPath);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        var snapshot = BuildSnapshot(report, name);
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        await File.WriteAllTextAsync(outputPath, json, cancellationToken);
        return new WorkflowSnapshotCreateResult(outputPath, snapshot.Name, report.ExecutionId);
    }

    public async Task<WorkflowSnapshotComparisonResult> CompareAsync(
        string executionReportPath,
        string snapshotPath,
        CancellationToken cancellationToken)
    {
        var report = await LoadReportAsync(executionReportPath, cancellationToken);
        var snapshot = await LoadSnapshotAsync(snapshotPath, cancellationToken);
        var actual = BuildSnapshot(report, snapshot.Name);
        var differences = new List<WorkflowSnapshotDifference>();

        CompareElements(
            "$",
            snapshot.Baseline,
            actual.Baseline,
            differences);

        return new WorkflowSnapshotComparisonResult(
            Path.GetFullPath(snapshotPath),
            differences.Count == 0,
            differences);
    }

    private static async Task<WorkflowExecutionReport> LoadReportAsync(
        string executionReportPath,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(executionReportPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Execution report was not found.", executionReportPath);
        }

        var rawJson = await File.ReadAllTextAsync(fullPath, cancellationToken);
        return JsonSerializer.Deserialize<WorkflowExecutionReport>(rawJson, JsonOptions)
               ?? throw new InvalidOperationException($"Execution report '{executionReportPath}' is empty.");
    }

    private static async Task<WorkflowExecutionSnapshot> LoadSnapshotAsync(
        string snapshotPath,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(snapshotPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Snapshot was not found.", snapshotPath);
        }

        var rawJson = await File.ReadAllTextAsync(fullPath, cancellationToken);
        return JsonSerializer.Deserialize<WorkflowExecutionSnapshot>(rawJson, JsonOptions)
               ?? throw new InvalidOperationException($"Snapshot '{snapshotPath}' is empty.");
    }

    private static WorkflowExecutionSnapshot BuildSnapshot(WorkflowExecutionReport report, string name)
    {
        var baseline = JsonSerializer.SerializeToElement(new
        {
            workflow = new
            {
                report.WorkflowName,
                report.WorkflowId,
                report.WorkflowVersion,
                report.Environment,
                report.Mocked,
                report.DryRun
            },
            inputs = report.Inputs.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase),
            result = report.Result,
            output = SortDictionary(report.Output),
            metrics = new
            {
                report.Metrics.TotalStages,
                report.Metrics.ExecutedStages,
                report.Metrics.SkippedStages,
                report.Metrics.FailedStages,
                report.Metrics.MockedStages,
                report.Metrics.HttpStages,
                report.Metrics.WorkflowStages,
                report.Metrics.JumpedStages,
                report.Metrics.TotalAssertions,
                report.Metrics.PassedAssertions,
                report.Metrics.FailedAssertions,
                report.Metrics.WarningAssertions
            },
            stages = report.Stages.Select(stage => new
            {
                stage.WorkflowName,
                stage.StageName,
                stage.StageKind,
                stage.Depth,
                stage.Status,
                stage.Mocked,
                stage.HttpStatusCode,
                stage.EnsureMode,
                stage.EnsureStatus,
                output = SortDictionary(stage.Output),
                workflowOutput = SortDictionary(stage.WorkflowOutput),
                assertions = stage.Assertions.Select(BuildAssertionSnapshot)
            }),
            assertions = report.Assertions.Select(BuildAssertionSnapshot),
            preflight = report.Preflight.Operations.Select(operation => new
            {
                operation.OperationType,
                operation.DefinitionName,
                operation.Target,
                operation.Status,
                operation.Message,
                operation.RetryCount,
                attempts = operation.Attempts.Select(attempt => new
                {
                    attempt.AttemptNumber,
                    attempt.RequestUri,
                    attempt.Status,
                    attempt.HttpStatusCode,
                    attempt.ErrorMessage
                })
            })
        }, JsonOptions);

        return new WorkflowExecutionSnapshot
        {
            SnapshotSchemaVersion = SnapshotSchemaVersion,
            Name = name,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            SourceExecutionId = report.ExecutionId,
            WorkflowName = report.WorkflowName,
            WorkflowId = report.WorkflowId,
            WorkflowVersion = report.WorkflowVersion,
            Environment = report.Environment,
            Timeline = BuildSnapshotTimeline(report),
            Baseline = baseline
        };
    }

    private static WorkflowExecutionSnapshotTimeline BuildSnapshotTimeline(WorkflowExecutionReport report)
    {
        var reportStart = report.StartedAtUtc;
        var stages = report.Stages.Select(stage => new WorkflowExecutionSnapshotStageTimeline
        {
            WorkflowName = stage.WorkflowName,
            StageName = stage.StageName,
            StageKind = stage.StageKind,
            Depth = stage.Depth,
            Status = stage.Status,
            HttpStatusCode = stage.HttpStatusCode,
            OffsetMs = Math.Max(0, (long)(stage.StartedAtUtc - reportStart).TotalMilliseconds),
            DurationMs = stage.DurationMs
        }).ToList();

        return new WorkflowExecutionSnapshotTimeline
        {
            DurationMs = report.DurationMs,
            Stages = stages
        };
    }

    private static object BuildAssertionSnapshot(WorkflowAssertionExecutionRecord assertion)
        => new
        {
            assertion.Scope,
            assertion.WorkflowName,
            assertion.StageName,
            assertion.Name,
            assertion.Status,
            assertion.Operator,
            assertion.Expression,
            assertion.Expected,
            assertion.Actual,
            assertion.Blocking,
            assertion.Message,
            assertion.WarningMessage
        };

    private static SortedDictionary<string, object?> SortDictionary(IReadOnlyDictionary<string, object?> values)
    {
        var sorted = new SortedDictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values)
        {
            sorted[pair.Key] = pair.Value;
        }

        return sorted;
    }

    private static string BuildDefaultSnapshotPath(
        string executionReportPath,
        WorkflowExecutionReport report,
        string snapshotName)
    {
        var reportDirectory = Path.GetDirectoryName(Path.GetFullPath(executionReportPath)) ?? ".";
        var baseDirectory = Path.GetDirectoryName(reportDirectory) ?? reportDirectory;
        var snapshotDirectory = Path.Combine(baseDirectory, "snapshots");
        var workflowName = SanitizePathSegment(report.WorkflowName);
        var name = SanitizePathSegment(snapshotName);
        return Path.Combine(snapshotDirectory, $"{workflowName}.{name}.workflow.snapshot.json");
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(value
            .Select(character => invalid.Contains(character) || char.IsWhiteSpace(character) ? '-' : character)
            .ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "snapshot" : clean;
    }

    private static void CompareElements(
        string path,
        JsonElement expected,
        JsonElement actual,
        List<WorkflowSnapshotDifference> differences)
    {
        if (differences.Count >= MaxDifferences)
        {
            return;
        }

        if (expected.ValueKind != actual.ValueKind)
        {
            AddDifference(path, expected, actual, differences);
            return;
        }

        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                CompareObjects(path, expected, actual, differences);
                break;
            case JsonValueKind.Array:
                CompareArrays(path, expected, actual, differences);
                break;
            default:
                if (expected.GetRawText() != actual.GetRawText())
                {
                    AddDifference(path, expected, actual, differences);
                }
                break;
        }
    }

    private static void CompareObjects(
        string path,
        JsonElement expected,
        JsonElement actual,
        List<WorkflowSnapshotDifference> differences)
    {
        var expectedProperties = expected.EnumerateObject()
            .ToDictionary(property => property.Name, StringComparer.Ordinal);
        var actualProperties = actual.EnumerateObject()
            .ToDictionary(property => property.Name, StringComparer.Ordinal);
        foreach (var propertyName in expectedProperties.Keys.Union(actualProperties.Keys).Order(StringComparer.Ordinal))
        {
            if (!expectedProperties.TryGetValue(propertyName, out var expectedProperty))
            {
                differences.Add(new WorkflowSnapshotDifference($"{path}.{propertyName}", "<missing>", ToDisplay(actualProperties[propertyName].Value)));
                continue;
            }

            if (!actualProperties.TryGetValue(propertyName, out var actualProperty))
            {
                differences.Add(new WorkflowSnapshotDifference($"{path}.{propertyName}", ToDisplay(expectedProperty.Value), "<missing>"));
                continue;
            }

            CompareElements($"{path}.{propertyName}", expectedProperty.Value, actualProperty.Value, differences);
        }
    }

    private static void CompareArrays(
        string path,
        JsonElement expected,
        JsonElement actual,
        List<WorkflowSnapshotDifference> differences)
    {
        var expectedItems = expected.EnumerateArray().ToArray();
        var actualItems = actual.EnumerateArray().ToArray();
        if (expectedItems.Length != actualItems.Length)
        {
            differences.Add(new WorkflowSnapshotDifference($"{path}.length", expectedItems.Length.ToString(), actualItems.Length.ToString()));
        }

        var count = Math.Min(expectedItems.Length, actualItems.Length);
        for (var i = 0; i < count; i++)
        {
            CompareElements($"{path}[{i}]", expectedItems[i], actualItems[i], differences);
        }
    }

    private static void AddDifference(
        string path,
        JsonElement expected,
        JsonElement actual,
        List<WorkflowSnapshotDifference> differences)
        => differences.Add(new WorkflowSnapshotDifference(path, ToDisplay(expected), ToDisplay(actual)));

    private static string ToDisplay(JsonElement element)
        => element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : element.GetRawText();
}

public sealed record WorkflowSnapshotCreateResult(
    string SnapshotPath,
    string SnapshotName,
    string SourceExecutionId);

public sealed record WorkflowSnapshotComparisonResult(
    string SnapshotPath,
    bool Matches,
    IReadOnlyList<WorkflowSnapshotDifference> Differences);

public sealed record WorkflowSnapshotDifference(
    string Path,
    string Expected,
    string Actual);

public sealed class WorkflowExecutionSnapshot
{
    public string SnapshotSchemaVersion { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public string SourceExecutionId { get; init; } = string.Empty;
    public string WorkflowName { get; init; } = string.Empty;
    public string WorkflowId { get; init; } = string.Empty;
    public string WorkflowVersion { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
    public WorkflowExecutionSnapshotTimeline? Timeline { get; init; }
    public JsonElement Baseline { get; init; }
}

public sealed class WorkflowExecutionSnapshotTimeline
{
    public long DurationMs { get; init; }
    public List<WorkflowExecutionSnapshotStageTimeline> Stages { get; init; } = [];
}

public sealed class WorkflowExecutionSnapshotStageTimeline
{
    public string WorkflowName { get; init; } = string.Empty;
    public string StageName { get; init; } = string.Empty;
    public string StageKind { get; init; } = string.Empty;
    public int Depth { get; init; }
    public string Status { get; init; } = string.Empty;
    public int? HttpStatusCode { get; init; }
    public long OffsetMs { get; init; }
    public long DurationMs { get; init; }
}
