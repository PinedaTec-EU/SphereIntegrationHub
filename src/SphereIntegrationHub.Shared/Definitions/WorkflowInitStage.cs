namespace SphereIntegrationHub.Definitions;

public sealed class WorkflowInitStage
{
    public List<WorkflowVariableDefinition> Variables { get; set; } = new();
    public Dictionary<string, string>? Context { get; set; }
}
