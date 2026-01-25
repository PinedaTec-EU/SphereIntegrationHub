using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.Services;

internal interface IRunIfEvaluator
{
    bool ShouldRunStage(WorkflowStageDefinition stage, ExecutionContext context);
}
