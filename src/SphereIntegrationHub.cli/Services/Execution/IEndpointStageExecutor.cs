using SphereIntegrationHub.Definitions;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SphereIntegrationHub.Services;

internal interface IEndpointStageExecutor
{
    Task<string?> ExecuteAsync(
        WorkflowDefinition definition,
        WorkflowStageDefinition stage,
        IReadOnlyDictionary<string, string> apiBaseUrls,
        ExecutionContext context,
        bool verbose,
        string workflowPath,
        bool mocked,
        CancellationToken cancellationToken);
}
