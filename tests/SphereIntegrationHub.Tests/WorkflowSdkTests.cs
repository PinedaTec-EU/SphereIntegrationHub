using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Sdk;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace SphereIntegrationHub.Tests;

public sealed class WorkflowSdkTests
{
    [Fact]
    public async Task Run_ResolvesDefaultCatalogAndWfvars_AndReturnsWorkflowOutputs()
    {
        using WireMockServer server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/api/accounts").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"id":"acc-123","name":"sdk-user"}"""));

        var root = Path.Combine(Path.GetTempPath(), $"sih-sdk-{Guid.NewGuid():N}");
        var workflowsPath = Path.Combine(root, "workflows");
        var cachePath = Path.Combine(root, "cache", "1.0");
        Directory.CreateDirectory(workflowsPath);
        Directory.CreateDirectory(cachePath);

        var workflowPath = Path.Combine(workflowsPath, "create-account.workflow");
        var varsFilePath = Path.Combine(workflowsPath, "create-account.wfvars");
        var catalogPath = Path.Combine(root, "api.catalog");
        var swaggerCachePath = Path.Combine(cachePath, "accounts.json");

        File.WriteAllText(workflowPath, """
version: "1.0"
id: "sdk-create-account"
name: "sdk-create-account"
references:
  apis:
    - name: accounts
      definition: accounts
input:
  - name: name
    type: Fixed
stages:
  - name: create-account
    kind: Endpoint
    apiRef: accounts
    endpoint: /api/accounts
    httpVerb: POST
    expectedStatus: 201
    body: '{"name":"{{input.name}}"}'
    output:
      accountId: "{{response.body.id}}"
endStage:
  output:
    accountId: "{{stage:create-account.output.accountId}}"
""");

        File.WriteAllText(varsFilePath, """
global:
  name: "sdk-user"
""");

        File.WriteAllText(catalogPath, $"""
- version: "1.0"
  definitions:
    - name: accounts
      swaggerUrl: "unused"
      baseUrl:
        dev: "{server.Url}"
""");

        File.WriteAllText(swaggerCachePath, """
{
  "openapi": "3.0.1",
  "paths": {
    "/api/accounts": {
      "post": {
        "responses": {
          "201": {
            "description": "Created"
          }
        }
      }
    }
  }
}
""");

        try
        {
            var result = await sihub
                .Run(workflowPath)
                .Environment("dev")
                .ExecuteAsync();

            Assert.Equal("acc-123", result.Output["accountId"]);
            Assert.Equal(workflowPath, result.WorkflowPath);
            Assert.Equal("1.0", result.CatalogVersion);
            Assert.Equal(catalogPath, result.CatalogPath);
            Assert.Equal(varsFilePath, result.VarsFilePath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Run_AllowsCatalogOverrideForTests()
    {
        using WireMockServer server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/api/accounts").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"id":"acc-456"}"""));

        var root = Path.Combine(Path.GetTempPath(), $"sih-sdk-override-{Guid.NewGuid():N}");
        var workflowsPath = Path.Combine(root, "workflows");
        var workflowPath = Path.Combine(workflowsPath, "create-account.workflow");
        var cachePath = Path.Combine(root, "cache", "1.0");
        Directory.CreateDirectory(workflowsPath);
        Directory.CreateDirectory(cachePath);

        File.WriteAllText(workflowPath, """
version: "1.0"
id: "sdk-create-account-override"
name: "sdk-create-account-override"
references:
  apis:
    - name: accounts
      definition: accounts
input:
  - name: name
    type: Fixed
stages:
  - name: create-account
    kind: Endpoint
    apiRef: accounts
    endpoint: /api/accounts
    httpVerb: POST
    expectedStatus: 201
    body: '{"name":"{{input.name}}"}'
    output:
      accountId: "{{response.body.id}}"
endStage:
  output:
    accountId: "{{stage:create-account.output.accountId}}"
""");

        File.WriteAllText(Path.Combine(cachePath, "accounts.json"), """
{
  "openapi": "3.0.1",
  "paths": {
    "/api/accounts": {
      "post": {
        "responses": {
          "201": {
            "description": "Created"
          }
        }
      }
    }
  }
}
""");

        var catalog = new ApiCatalogVersion
        {
            Version = "1.0",
            Definitions =
            [
                new ApiDefinition
                {
                    Name = "accounts",
                    SwaggerUrl = "unused",
                    BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["test"] = server.Url!
                    }
                }
            ]
        };

        try
        {
            var result = await sih
                .Run(workflowPath)
                .Catalog(catalog)
                .Environment("test")
                .Input("name", "override-user")
                .ExecuteAsync();

            Assert.Equal("acc-456", result.Output["accountId"]);
            Assert.Null(result.CatalogPath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
