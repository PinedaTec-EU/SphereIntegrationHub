using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.cli;

internal interface ICliWorkflowEnvironmentValidator
{
    List<string> Validate(WorkflowDefinition workflow, ApiCatalogVersion catalogVersion, string environment);
}
