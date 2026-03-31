using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;

using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace SphereIntegrationHub.Tests;

public sealed class WorkflowExecutorIdempotencyTests
{
    [Fact]
    public async Task ExecuteAsync_EnsureCreateIfMissingAddsSemanticOutputsAndJumps()
    {
        using WireMockServer server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/api/accounts").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(409)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"reason\":\"exists\"}"));
        server
            .Given(Request.Create().WithPath("/api/accounts/existing").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"id\":\"existing\"}"));

        var definition = new WorkflowDefinition
        {
            Version = "3.11",
            Id = "test-workflow",
            Name = "test-workflow",
            References = new WorkflowReference
            {
                Apis = new List<ApiReferenceItem>
                {
                    new() { Name = "accounts", Definition = "accounts" }
                }
            },
            Output = true,
            Stages = new List<WorkflowStageDefinition>
            {
                new()
                {
                    Name = "create-account",
                    Kind = WorkflowStageKind.Endpoint,
                    ApiRef = "accounts",
                    Endpoint = "/api/accounts",
                    HttpVerb = "POST",
                    ExpectedStatus = 201,
                    Ensure = new WorkflowStageEnsureDefinition
                    {
                        Mode = "CreateIfMissing",
                        JumpTo = "load-account",
                        Output = new Dictionary<string, string>
                        {
                            ["exists"] = "true"
                        }
                    }
                },
                new()
                {
                    Name = "load-account",
                    Kind = WorkflowStageKind.Endpoint,
                    ApiRef = "accounts",
                    Endpoint = "/api/accounts/existing",
                    HttpVerb = "GET",
                    ExpectedStatus = 200
                }
            },
            EndStage = new WorkflowEndStage
            {
                Output = new Dictionary<string, string>
                {
                    ["exists"] = "{{stage:create-account.output.exists}}",
                    ["ensure_status"] = "{{stage:create-account.output.ensure_status}}",
                    ["existed"] = "{{stage:create-account.output.existed}}"
                }
            }
        };

        var document = new WorkflowDocument(definition, "/tmp/test.workflow", new Dictionary<string, string>());
        var catalogVersion = new ApiCatalogVersion
        {
            Version = "test",
            BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["test"] = server.Url!
            },
            Definitions = new List<ApiDefinition>
            {
                new ApiDefinition { Name = "accounts", SwaggerUrl = "http://unused" }
            }
        };

        using var httpClient = new HttpClient();
        var executor = new WorkflowExecutor(httpClient, new DynamicValueService());

        var result = await executor.ExecuteAsync(
            document,
            catalogVersion,
            "test",
            new Dictionary<string, string>(),
            varsOverrideActive: false,
            mocked: false,
            verbose: false,
            debug: false,
            cancellationToken: CancellationToken.None);

        Assert.Equal("true", result.Output["exists"]);
        Assert.Equal("existing", result.Output["ensure_status"]);
        Assert.Equal("true", result.Output["existed"]);
        Assert.Equal(2, server.LogEntries.Count());
    }

    [Fact]
    public async Task ExecuteAsync_OnStatusJumpRunsBeforeExpectedStatusFailure()
    {
        using WireMockServer server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/api/accounts").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(409)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"reason\":\"exists\"}"));
        server
            .Given(Request.Create().WithPath("/api/accounts/existing").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"id\":\"existing\"}"));

        var definition = new WorkflowDefinition
        {
            Version = "3.11",
            Id = "test-workflow",
            Name = "test-workflow",
            References = new WorkflowReference
            {
                Apis = new List<ApiReferenceItem>
                {
                    new()
                    {
                        Name = "accounts",
                        Definition = "accounts"
                    }
                }
            },
            Output = true,
            Stages = new List<WorkflowStageDefinition>
            {
                new()
                {
                    Name = "create-account",
                    Kind = WorkflowStageKind.Endpoint,
                    ApiRef = "accounts",
                    Endpoint = "/api/accounts",
                    HttpVerb = "POST",
                    ExpectedStatuses = new[] { 200, 201, 409 },
                    OnStatus = new Dictionary<int, WorkflowStageStatusAction>
                    {
                        [409] = new()
                        {
                            JumpTo = "load-account",
                            Output = new Dictionary<string, string>
                            {
                                ["exists"] = "true"
                            }
                        }
                    }
                },
                new()
                {
                    Name = "load-account",
                    Kind = WorkflowStageKind.Endpoint,
                    ApiRef = "accounts",
                    Endpoint = "/api/accounts/existing",
                    HttpVerb = "GET",
                    ExpectedStatus = 200
                }
            },
            EndStage = new WorkflowEndStage
            {
                Output = new Dictionary<string, string>
                {
                    ["exists"] = "{{stage:create-account.output.exists}}",
                    ["http_status"] = "{{stage:create-account.output.http_status}}"
                }
            }
        };

        var document = new WorkflowDocument(definition, "/tmp/test.workflow", new Dictionary<string, string>());
        var catalogVersion = new ApiCatalogVersion
        {
            Version = "test",
            BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["test"] = server.Url!
            },
            Definitions = new List<ApiDefinition>
            {
                new ApiDefinition { Name = "accounts", SwaggerUrl = "http://unused" }
            }
        };

        using var httpClient = new HttpClient();
        var executor = new WorkflowExecutor(httpClient, new DynamicValueService());

        var result = await executor.ExecuteAsync(
            document,
            catalogVersion,
            "test",
            new Dictionary<string, string>(),
            varsOverrideActive: false,
            mocked: false,
            verbose: false,
            debug: false,
            cancellationToken: CancellationToken.None);

        Assert.Equal("true", result.Output["exists"]);
        Assert.Equal("409", result.Output["http_status"]);
        Assert.Equal(2, server.LogEntries.Count());
    }
}
