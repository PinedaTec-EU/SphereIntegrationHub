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
        if (catalogVersion.Definitions.Count == 0)
        {
            return errors;
        }

        foreach (var definition in catalogVersion.Definitions)
        {
            if (!ApiBaseUrlResolver.TryResolveBaseUrl(catalogVersion, definition, environment, out _))
            {
                errors.Add($"Environment '{environment}' was not found for API definition '{definition.Name}' in catalog version '{catalogVersion.Version}'.");
            }
        }

        foreach (var connection in catalogVersion.Connections ?? Enumerable.Empty<ApiConnectionDefinition>())
        {
            if (connection.BaseUrl is null ||
                !ApiBaseUrlResolver.TryResolveBaseUrl(connection.BaseUrl, environment, out _))
            {
                errors.Add($"Environment '{environment}' was not found for connection '{connection.Name}' in catalog version '{catalogVersion.Version}'.");
            }
        }

        return errors;
    }
}
