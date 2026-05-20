namespace SphereIntegrationHub.Sdk;

public static class sihub
{
    public static WorkflowRunBuilder Run(string workflowPath)
        => new(workflowPath);
}
