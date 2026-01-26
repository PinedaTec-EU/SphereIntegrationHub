using System.Collections.Generic;

namespace SphereIntegrationHub.cli;

internal sealed class WorkflowConfig
{
    public WorkflowFeaturesConfig Features { get; set; } = new();
    public OpenTelemetryConfig OpenTelemetry { get; set; } = new();
    public List<string> Plugins { get; set; } = new();

    [YamlDotNet.Serialization.YamlIgnore]
    public string? ConfigPath { get; set; }
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
