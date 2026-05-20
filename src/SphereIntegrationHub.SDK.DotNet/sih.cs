namespace SphereIntegrationHub.Sdk;

public static class sih
{
    public static WorkflowRunBuilder Run(string workflowPath)
        => sihub.Run(workflowPath);
}
