using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;

namespace SphereIntegrationHub.Tests;

[Collection(CliCacheMetricsCollection.Name)]
public sealed class ApiEndpointValidatorPathParamTests : IDisposable
{
    private readonly string _cacheRoot;

    public ApiEndpointValidatorPathParamTests()
    {
        _cacheRoot = Path.Combine(Path.GetTempPath(), $"sih-path-param-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_cacheRoot);
    }

    [Fact]
    public void Validate_SwaggerStylePlaceholder_MatchesByName()
    {
        WriteSwagger(_cacheRoot, "accounts", """
        {
          "paths": {
            "/accounts/{accountId}": {
              "get": { "parameters": [{ "name": "accountId", "in": "path", "required": true }] }
            }
          }
        }
        """);

        var errors = RunValidate(endpoint: "/accounts/{accountId}", verb: "GET");

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_TemplateInputPlaceholder_MatchesByLastSegment()
    {
        WriteSwagger(_cacheRoot, "accounts", """
        {
          "paths": {
            "/accounts/{accountId}": {
              "get": { "parameters": [{ "name": "accountId", "in": "path", "required": true }] }
            }
          }
        }
        """);

        var errors = RunValidate(endpoint: "/accounts/{{inputs.accountId}}", verb: "GET");

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_TemplateStepOutputPlaceholder_MatchesByLastSegment()
    {
        WriteSwagger(_cacheRoot, "accounts", """
        {
          "paths": {
            "/accounts/{accountId}": {
              "get": { "parameters": [{ "name": "accountId", "in": "path", "required": true }] }
            }
          }
        }
        """);

        var errors = RunValidate(endpoint: "/accounts/{{steps.createAccount.output.accountId}}", verb: "GET");

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_TemplatePlaceholderNameMismatch_ReturnsError()
    {
        WriteSwagger(_cacheRoot, "accounts", """
        {
          "paths": {
            "/accounts/{accountId}": {
              "get": { "parameters": [{ "name": "accountId", "in": "path", "required": true }] }
            }
          }
        }
        """);

        // "id" != "accountId"
        var errors = RunValidate(endpoint: "/accounts/{{inputs.id}}", verb: "GET");

        Assert.Single(errors);
        Assert.Contains("accountId", errors[0]);
    }

    [Fact]
    public void Validate_NoPlaceholderForRequiredPathParam_EndpointNotFound()
    {
        // A hardcoded value in a path segment doesn't normalize to {}, so the
        // endpoint itself is not found in Swagger — the error surfaces as endpoint-not-found,
        // not as a missing path param. Name-based param validation only runs when the
        // endpoint IS found (i.e., at least one placeholder is present for the segment).
        WriteSwagger(_cacheRoot, "accounts", """
        {
          "paths": {
            "/accounts/{accountId}": {
              "get": { "parameters": [{ "name": "accountId", "in": "path", "required": true }] }
            }
          }
        }
        """);

        var errors = RunValidate(endpoint: "/accounts/hardcoded-value", verb: "GET");

        Assert.Single(errors);
        Assert.Contains("was not found in swagger", errors[0]);
    }

    [Fact]
    public void Validate_MultiplePathParams_AllMatched_NoErrors()
    {
        WriteSwagger(_cacheRoot, "orders", """
        {
          "paths": {
            "/tenants/{tenantId}/orders/{orderId}": {
              "get": {
                "parameters": [
                  { "name": "tenantId", "in": "path", "required": true },
                  { "name": "orderId", "in": "path", "required": true }
                ]
              }
            }
          }
        }
        """);

        var errors = RunValidate(
            apiName: "orders",
            definitionName: "orders",
            endpoint: "/tenants/{{inputs.tenantId}}/orders/{{steps.createOrder.output.orderId}}",
            verb: "GET");

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_MultiplePathParams_OneMissing_ReturnsError()
    {
        WriteSwagger(_cacheRoot, "orders", """
        {
          "paths": {
            "/tenants/{tenantId}/orders/{orderId}": {
              "get": {
                "parameters": [
                  { "name": "tenantId", "in": "path", "required": true },
                  { "name": "orderId", "in": "path", "required": true }
                ]
              }
            }
          }
        }
        """);

        // Only tenantId provided, orderId missing
        var errors = RunValidate(
            apiName: "orders",
            definitionName: "orders",
            endpoint: "/tenants/{{inputs.tenantId}}/orders/{{inputs.id}}",
            verb: "GET");

        Assert.Single(errors);
        Assert.Contains("orderId", errors[0]);
    }

    [Fact]
    public void Validate_OptionalPathParam_NotRequired_NoError()
    {
        WriteSwagger(_cacheRoot, "accounts", """
        {
          "paths": {
            "/accounts/{accountId}": {
              "get": { "parameters": [{ "name": "accountId", "in": "path", "required": false }] }
            }
          }
        }
        """);

        var errors = RunValidate(endpoint: "/accounts/{{inputs.id}}", verb: "GET");

        Assert.Empty(errors);
    }

    public void Dispose()
    {
        try { Directory.Delete(_cacheRoot, recursive: true); }
        catch { /* ignore cleanup failures */ }
    }

    private IReadOnlyList<string> RunValidate(
        string endpoint,
        string verb,
        string apiName = "accounts",
        string definitionName = "accounts")
    {
        var validator = new ApiEndpointValidator();
        var workflow = new WorkflowDefinition
        {
            References = new WorkflowReference
            {
                Apis = [new ApiReferenceItem { Name = apiName, Definition = definitionName }]
            },
            Stages =
            [
                new WorkflowStageDefinition
                {
                    Name = "test-stage",
                    Kind = WorkflowStageKind.Endpoint,
                    ApiRef = apiName,
                    Endpoint = endpoint,
                    HttpVerb = verb
                }
            ]
        };

        var catalog = new ApiCatalogVersion
        {
            Version = "v1",
            Definitions =
            [
                new ApiDefinition
                {
                    Name = definitionName,
                    SwaggerUrl = $"{definitionName}.json",
                    BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["test"] = "https://example.test" }
                }
            ]
        };

        return validator.Validate(workflow, catalog, _cacheRoot, validateRequiredParameters: true, verbose: false);
    }

    private static void WriteSwagger(string cacheRoot, string name, string json)
    {
        File.WriteAllText(Path.Combine(cacheRoot, $"{name}.json"), json);
    }
}
