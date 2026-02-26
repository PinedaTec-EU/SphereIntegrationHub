using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;
using SphereIntegrationHub.Services.Interfaces;

namespace SphereIntegrationHub.Tests;

public sealed class ApiEndpointValidatorLoggerTests
{
    [Fact]
    public void Validate_UsesLoggerWhenVerbose()
    {
        var logger = new TestLogger();
        var validator = new ApiEndpointValidator(logger);
        var workflow = new WorkflowDefinition
        {
            References = new WorkflowReference
            {
                Apis = new List<ApiReferenceItem>
                {
                    new() { Name = "accounts", Definition = "accounts" }
                }
            },
            Stages = new List<WorkflowStageDefinition>
            {
                new()
                {
                    Name = "create",
                    Kind = WorkflowStageKind.Endpoint,
                    ApiRef = "accounts",
                    Endpoint = "/accounts/{id}",
                    HttpVerb = "GET"
                }
            }
        };

        var catalog = new ApiCatalogVersion
        {
            Version = "v1",
            BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["test"] = "https://example.test"
            },
            Definitions = new List<ApiDefinition>
            {
                new ApiDefinition { Name = "accounts", SwaggerUrl = "swagger.json" }
            }
        };

        var cacheRoot = Path.Combine(Path.GetTempPath(), $"aos-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cacheRoot);
        var swaggerPath = Path.Combine(cacheRoot, "accounts.json");
        File.WriteAllText(swaggerPath, """
        {
          "paths": {
            "/accounts/{accountId}": {
              "get": {
                "parameters": []
              }
            }
          }
        }
        """);

        var errors = validator.Validate(workflow, catalog, cacheRoot, validateRequiredParameters: false, verbose: true);

        Assert.Empty(errors);
        Assert.Contains(logger.Messages, message => message.Contains("Validated endpoint stage", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_EndpointWithoutBasePath_MatchesSwaggerUsingDefinitionBasePath()
    {
        var validator = new ApiEndpointValidator();
        var workflow = new WorkflowDefinition
        {
            References = new WorkflowReference
            {
                Apis = new List<ApiReferenceItem>
                {
                    new() { Name = "licensing", Definition = "licensing" }
                }
            },
            Stages = new List<WorkflowStageDefinition>
            {
                new()
                {
                    Name = "create-tier",
                    Kind = WorkflowStageKind.Endpoint,
                    ApiRef = "licensing",
                    Endpoint = "/licensing/tiers",
                    HttpVerb = "POST"
                }
            }
        };

        var catalog = new ApiCatalogVersion
        {
            Version = "v1",
            BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["local"] = "https://localhost"
            },
            Definitions = new List<ApiDefinition>
            {
                new ApiDefinition
                {
                    Name = "licensing",
                    SwaggerUrl = "swagger.json",
                    BasePath = "/api"
                }
            }
        };

        var cacheRoot = Path.Combine(Path.GetTempPath(), $"aos-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cacheRoot);
        var swaggerPath = Path.Combine(cacheRoot, "licensing.json");
        File.WriteAllText(swaggerPath, """
        {
          "paths": {
            "/api/licensing/tiers": {
              "post": {
                "parameters": []
              }
            }
          }
        }
        """);

        var errors = validator.Validate(workflow, catalog, cacheRoot, validateRequiredParameters: false, verbose: false);

        Assert.Empty(errors);
    }

    private sealed class TestLogger : IExecutionLogger
    {
        public List<string> Messages { get; } = new();

        public void Info(string message) => Messages.Add(message);

        public void Error(string message) => Messages.Add(message);
    }
}
