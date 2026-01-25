namespace SphereIntegrationHub.Definitions;

public sealed class WorkflowEndStage
{
    public Dictionary<string, string> Output { get; set; } = new();
    public bool OutputJson { get; set; } = true;
    public Dictionary<string, string>? Context { get; set; }
    public WorkflowResultDefinition? Result { get; set; }
}

public sealed class WorkflowResultDefinition
{
    public string? Message { get; set; }
}
