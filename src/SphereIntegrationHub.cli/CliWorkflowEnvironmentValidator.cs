using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;

namespace SphereIntegrationHub.cli;

internal sealed class CliWorkflowEnvironmentValidator : ICliWorkflowEnvironmentValidator
{
    public List<string> Validate(
        WorkflowDefinition workflow,
        ApiCatalogVersion catalogVersion,
        string environment)
    {
        var errors = new List<string>();
        if (workflow.References?.Apis is null || workflow.References.Apis.Count == 0)
        {
            return errors;
        }

        foreach (var apiReference in workflow.References.Apis)
        {
            var definition = catalogVersion.Definitions.FirstOrDefault(def =>
                string.Equals(def.Name, apiReference.Definition, StringComparison.OrdinalIgnoreCase));
            if (definition is null)
            {
                errors.Add($"API definition '{apiReference.Definition}' was not found in catalog version '{catalogVersion.Version}'.");
                continue;
            }

            if (!ApiBaseUrlResolver.TryResolveBaseUrl(catalogVersion, definition, environment, out _))
            {
                errors.Add($"Environment '{environment}' was not found for API definition '{definition.Name}' in catalog version '{catalogVersion.Version}'.");
            }
        }

        return errors;
    }
}
