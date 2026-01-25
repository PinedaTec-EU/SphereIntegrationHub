namespace SphereIntegrationHub.Definitions;

public sealed class WorkflowInputDefinition
{
    public string Name { get; set; } = string.Empty;
    public RandomValueType Type { get; set; }
    public bool Required { get; set; } = true;
    public string? Description { get; set; }
}
