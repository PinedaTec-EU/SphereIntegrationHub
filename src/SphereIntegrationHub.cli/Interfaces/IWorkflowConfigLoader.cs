namespace SphereIntegrationHub.cli;

internal interface IWorkflowConfigLoader
{
    WorkflowConfig Load(string workflowPath);
}
