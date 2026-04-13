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
    /// <summary>
    /// Named derived variables that can be referenced with {{var:name}} anywhere in the workflow.
    /// Each value is a template string evaluated lazily at the point of reference using the
    /// current execution context (stage outputs, globals, inputs, etc.).
    /// Useful for unifying mutually-exclusive stage branches into a single named token.
    /// </summary>
    public Dictionary<string, string>? Vars { get; set; }
}
