namespace SphereIntegrationHub.cli;

internal sealed class WorkflowConfig
{
    public WorkflowFeaturesConfig Features { get; set; } = new();
    public OpenTelemetryConfig OpenTelemetry { get; set; } = new();
}

internal sealed class WorkflowFeaturesConfig
{
    public bool OpenTelemetry { get; set; } = CliConstants.OpenTelemetryDefaultEnabled;
}

internal sealed class OpenTelemetryConfig
{
    public string? ServiceName { get; set; }
    public string? Endpoint { get; set; }
    public bool ConsoleExporter { get; set; }
    public bool DebugConsole { get; set; }
}
