using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.cli;

namespace SphereIntegrationHub.Tests;

public sealed class CliWorkflowEnvironmentValidatorTests
{
    [Fact]
    public void Validate_NoApis_ReturnsEmpty()
    {
        ICliWorkflowEnvironmentValidator validator = new CliWorkflowEnvironmentValidator();
        var workflow = new WorkflowDefinition();
        var catalog = new ApiCatalogVersion { Version = "v1", Definitions = new List<ApiDefinition>() };

        var errors = validator.Validate(workflow, catalog, "dev");

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_MissingEnvironmentInCatalogDefinition_ReturnsError()
    {
        ICliWorkflowEnvironmentValidator validator = new CliWorkflowEnvironmentValidator();
        var workflow = new WorkflowDefinition();
        var catalog = new ApiCatalogVersion
        {
            Version = "v1",
            Definitions = new List<ApiDefinition>
            {
                new ApiDefinition { Name = "accounts", SwaggerUrl = "swagger.json", BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["prod"] = "https://example.test" } }
            }
        };

        var errors = validator.Validate(workflow, catalog, "dev");

        Assert.Contains(errors, error => error.Contains("Environment", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_CatalogHasOneMissingEnvironmentAmongMany_ReturnsErrorForThatDefinition()
    {
        ICliWorkflowEnvironmentValidator validator = new CliWorkflowEnvironmentValidator();
        var workflow = new WorkflowDefinition();
        var catalog = new ApiCatalogVersion
        {
            Version = "v1",
            Definitions = new List<ApiDefinition>
            {
                new ApiDefinition { Name = "accounts", SwaggerUrl = "swagger.json", BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["dev"] = "https://example.test" } },
                new ApiDefinition { Name = "orders", SwaggerUrl = "swagger.json", BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["prod"] = "https://example.test" } }
            }
        };

        var errors = validator.Validate(workflow, catalog, "dev");

        Assert.Single(errors);
        Assert.Contains(errors, error => error.Contains("orders", StringComparison.OrdinalIgnoreCase));
    }
}
