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
        var catalog = new ApiCatalogVersion("v1", new Dictionary<string, string>(), new List<ApiDefinition>());

        var errors = validator.Validate(workflow, catalog, "dev");

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_MissingApiDefinition_ReturnsError()
    {
        ICliWorkflowEnvironmentValidator validator = new CliWorkflowEnvironmentValidator();
        var workflow = new WorkflowDefinition
        {
            References = new WorkflowReference
            {
                Apis = new List<ApiReferenceItem>
                {
                    new() { Name = "accounts", Definition = "accounts" }
                }
            }
        };
        var catalog = new ApiCatalogVersion("v1", new Dictionary<string, string>(), new List<ApiDefinition>());

        var errors = validator.Validate(workflow, catalog, "dev");

        Assert.Contains(errors, error => error.Contains("API definition", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_MissingEnvironment_ReturnsError()
    {
        ICliWorkflowEnvironmentValidator validator = new CliWorkflowEnvironmentValidator();
        var workflow = new WorkflowDefinition
        {
            References = new WorkflowReference
            {
                Apis = new List<ApiReferenceItem>
                {
                    new() { Name = "accounts", Definition = "accounts" }
                }
            }
        };
        var catalog = new ApiCatalogVersion(
            "v1",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["prod"] = "https://example.test"
            },
            new List<ApiDefinition>
            {
                new("accounts", "swagger.json", null, null)
            });

        var errors = validator.Validate(workflow, catalog, "dev");

        Assert.Contains(errors, error => error.Contains("Environment", StringComparison.OrdinalIgnoreCase));
    }
}
