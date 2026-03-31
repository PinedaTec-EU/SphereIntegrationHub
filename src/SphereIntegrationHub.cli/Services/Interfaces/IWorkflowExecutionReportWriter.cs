using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.Services.Interfaces;

public interface IWorkflowExecutionReportWriter
{
    Task<WorkflowExecutionArtifacts> WriteAsync(
        WorkflowExecutionReport report,
        WorkflowDocument document,
        WorkflowExecutionReportOptions options,
        CancellationToken cancellationToken);
}
