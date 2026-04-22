namespace SphereIntegrationHub.Definitions;

public static class WorkflowStageKind
{
    public const string Endpoint = "Endpoint";
    public const string Workflow = "Workflow";
    public const string Http = "Http";

    public static bool IsWorkflow(string? kind)
        => string.Equals(kind, Workflow, StringComparison.OrdinalIgnoreCase);
}
