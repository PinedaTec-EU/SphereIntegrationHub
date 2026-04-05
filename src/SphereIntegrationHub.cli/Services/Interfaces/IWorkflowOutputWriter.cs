using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.Services.Interfaces;

public interface IWorkflowOutputWriter
{
    Task<string?> WriteOutputAsync(
        WorkflowDefinition definition,
        WorkflowDocument document,
        string executionId,
        IReadOnlyDictionary<string, string> outputs,
        CancellationToken cancellationToken);
}
