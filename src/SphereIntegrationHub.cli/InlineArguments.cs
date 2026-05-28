namespace SphereIntegrationHub.cli;

internal sealed record InlineArguments(
    string? WorkflowPath = null,
    string? Environment = null,
    string? CatalogPath = null,
    string? EnvFileOverride = null,
    string? VarsFilePath = null,
    string? ReportFormat = null,
    string? CaptureHttp = null,
    bool RefreshCache = false,
    bool DryRun = false,
    bool Verbose = false,
    bool Debug = false,
    bool Mocked = false,
    bool? RedactSensitiveData = null,
    bool? SummaryConsole = null,
    bool? AssertionFailuresBlock = null,
    Dictionary<string, string>? Inputs = null,
    bool ShowHelp = false,
    bool ShowVersion = false,
    string? Error = null,
    bool IsReportCommand = false,
    string? ExecutionReportPath = null,
    string? ReportOutputPath = null,
    bool OpenAfterGenerate = true,
    bool IsSnapshotCommand = false,
    string? SnapshotAction = null,
    string? SnapshotPath = null,
    string? SnapshotName = null)
{
    public Dictionary<string, string> Inputs { get; init; } =
        Inputs ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
