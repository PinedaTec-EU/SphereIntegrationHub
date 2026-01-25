namespace SphereIntegrationHub.Definitions;

public sealed class WorkflowReference
{
    public List<WorkflowReferenceItem>? Workflows { get; set; }
    public List<ApiReferenceItem>? Apis { get; set; }
    public string? EnvironmentFile { get; set; }
}

public sealed class WorkflowReferenceItem
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}

public sealed class ApiReferenceItem
{
    public string Name { get; set; } = string.Empty;
    public string Definition { get; set; } = string.Empty;
}
