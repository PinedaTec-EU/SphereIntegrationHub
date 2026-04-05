namespace SphereIntegrationHub.Definitions;

public sealed class WorkflowDefinition
{
    public string Version { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public WorkflowReference? References { get; set; }
    public List<WorkflowInputDefinition>? Input { get; set; }
    public bool Output { get; set; }
    public WorkflowInitStage? InitStage { get; set; }
    public WorkflowResilienceDefinition? Resilience { get; set; }
    public List<WorkflowStageDefinition>? Stages { get; set; }
    public WorkflowEndStage? EndStage { get; set; }
}
