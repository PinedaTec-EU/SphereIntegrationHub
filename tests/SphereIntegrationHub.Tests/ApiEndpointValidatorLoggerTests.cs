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

        var catalog = new ApiCatalogVersion(
            "v1",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["test"] = "https://example.test"
            },
            new List<ApiDefinition>
            {
                new("accounts", "swagger.json", null, null)
            });

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
        Assert.Contains(logger.Messages, message => message.Contains("matched swagger path", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class TestLogger : IExecutionLogger
    {
        public List<string> Messages { get; } = new();

        public void Info(string message) => Messages.Add(message);

        public void Error(string message) => Messages.Add(message);
    }
}
