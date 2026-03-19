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
        var catalog = new ApiCatalogVersion { Version = "v1", BaseUrl = new Dictionary<string, string>(), Definitions = new List<ApiDefinition>() };

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
        var catalog = new ApiCatalogVersion { Version = "v1", BaseUrl = new Dictionary<string, string>(), Definitions = new List<ApiDefinition>() };

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
        var catalog = new ApiCatalogVersion
        {
            Version = "v1",
            BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["prod"] = "https://example.test"
            },
            Definitions = new List<ApiDefinition>
            {
                new ApiDefinition { Name = "accounts", SwaggerUrl = "swagger.json" }
            }
        };

        var errors = validator.Validate(workflow, catalog, "dev");

        Assert.Contains(errors, error => error.Contains("Environment", StringComparison.OrdinalIgnoreCase));
    }
}
