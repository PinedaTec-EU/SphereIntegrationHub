using System.Collections.Generic;

using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.Services.Plugins;

public interface IStageValidator
{
    string Id { get; }
    IReadOnlyCollection<string> StageKinds { get; }

    void ValidateStage(WorkflowStageDefinition stage, StageValidationContext context, List<string> errors);
}
