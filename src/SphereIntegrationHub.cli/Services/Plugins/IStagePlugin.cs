using System.Threading;
using System.Threading.Tasks;

using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.Services.Plugins;

public interface IStagePlugin
{
    string Id { get; }
    IReadOnlyCollection<string> StageKinds { get; }
    StagePluginCapabilities Capabilities { get; }

    Task<string?> ExecuteAsync(WorkflowStageDefinition stage, StageExecutionContext context, CancellationToken cancellationToken);
}
