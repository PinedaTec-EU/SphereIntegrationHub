using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.Services.Interfaces;

public interface IWorkflowOutputWriter
{
    Task<string?> WriteOutputAsync(
        WorkflowDefinition definition,
        WorkflowDocument document,
        IReadOnlyDictionary<string, string> outputs,
        CancellationToken cancellationToken);
}
