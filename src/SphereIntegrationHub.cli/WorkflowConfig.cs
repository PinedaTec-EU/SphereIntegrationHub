namespace SphereIntegrationHub.cli;

internal sealed class WorkflowConfig
{
    public WorkflowFeaturesConfig Features { get; set; } = new();
    public OpenTelemetryConfig OpenTelemetry { get; set; } = new();
    public ReportingConfig Reporting { get; set; } = new();
}

internal sealed class WorkflowFeaturesConfig
{
    public bool OpenTelemetry { get; set; } = CliConstants.OpenTelemetryDefaultEnabled;
}

internal sealed class ReportingConfig
{
    public bool Enabled { get; set; } = true;
    public string Format { get; set; } = "json";
    public string CaptureHttp { get; set; } = "headers";
    public bool RedactSensitiveData { get; set; } = true;
    public bool SummaryConsole { get; set; } = true;
}

internal sealed class OpenTelemetryConfig
{
    public string? ServiceName { get; set; }
    public string? Endpoint { get; set; }
    public bool ConsoleExporter { get; set; }
    public bool DebugConsole { get; set; }
}
