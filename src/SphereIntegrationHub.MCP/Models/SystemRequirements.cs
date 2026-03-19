namespace SphereIntegrationHub.MCP.Models;

/// <summary>
/// Requirements for system synthesis
/// </summary>
public sealed record SystemRequirements
{
    public List<string> RequiredApis { get; set; } = [];
    public List<string> PreferredApis { get; set; } = [];
    public int MaxStagesPerWorkflow { get; set; } = 10;
    public bool IncludeAuthentication { get; set; } = true;
    public bool IncludeErrorHandling { get; set; } = true;
    public bool IncludeRetries { get; set; } = true;
    public List<string> ExcludeEndpoints { get; set; } = [];
    public string? PerformanceTarget { get; set; } // "fast", "balanced", "thorough"
}
