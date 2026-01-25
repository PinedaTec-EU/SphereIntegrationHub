using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;

using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace SphereIntegrationHub.Tests;

public sealed class WorkflowExecutorRunIfTests
{
    [Fact]
    public async Task ExecuteAsync_RunIfInListTrue_ExecutesStage()
    {
        using WireMockServer server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/first").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200));
        server
            .Given(Request.Create().WithPath("/second").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(201));

        var definition = BuildDefinition(
            runIf: "{{stage:first.output.http_status}} in [200, 201]");

        var document = new WorkflowDocument(
            definition,
            "/tmp/test.workflow",
            new Dictionary<string, string>());

        var catalogVersion = BuildCatalogVersion(server);

        using var httpClient = new HttpClient();
        var executor = new WorkflowExecutor(httpClient, new DynamicValueService());

        await executor.ExecuteAsync(
            document,
            catalogVersion,
            "test",
            new Dictionary<string, string>(),
            varsOverrideActive: false,
            mocked: false,
            verbose: false,
            debug: false,
            cancellationToken: CancellationToken.None);

        Assert.Equal(2, server.LogEntries.Count());
    }

    [Fact]
    public async Task ExecuteAsync_RunIfInListFalse_SkipsStage()
    {
        using WireMockServer server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/first").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200));
        server
            .Given(Request.Create().WithPath("/second").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(201));

        var definition = BuildDefinition(
            runIf: "{{stage:first.output.http_status}} in [500]");

        var document = new WorkflowDocument(
            definition,
            "/tmp/test.workflow",
            new Dictionary<string, string>());

        var catalogVersion = BuildCatalogVersion(server);

        using var httpClient = new HttpClient();
        var executor = new WorkflowExecutor(httpClient, new DynamicValueService());

        await executor.ExecuteAsync(
            document,
            catalogVersion,
            "test",
            new Dictionary<string, string>(),
            varsOverrideActive: false,
            mocked: false,
            verbose: false,
            debug: false,
            cancellationToken: CancellationToken.None);

        Assert.Single(server.LogEntries);
    }

    [Fact]
    public async Task ExecuteAsync_RunIfNotInListTrue_ExecutesStage()
    {
        using WireMockServer server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/first").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200));
        server
            .Given(Request.Create().WithPath("/second").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(201));

        var definition = BuildDefinition(
            runIf: "{{stage:first.output.http_status}} not in [500]");

        var document = new WorkflowDocument(
            definition,
            "/tmp/test.workflow",
            new Dictionary<string, string>());

        var catalogVersion = BuildCatalogVersion(server);

        using var httpClient = new HttpClient();
        var executor = new WorkflowExecutor(httpClient, new DynamicValueService());

        await executor.ExecuteAsync(
            document,
            catalogVersion,
            "test",
            new Dictionary<string, string>(),
            varsOverrideActive: false,
            mocked: false,
            verbose: false,
            debug: false,
            cancellationToken: CancellationToken.None);

        Assert.Equal(2, server.LogEntries.Count());
    }

    [Fact]
    public async Task ExecuteAsync_RunIfNotInListFalse_SkipsStage()
    {
        using WireMockServer server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/first").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200));
        server
            .Given(Request.Create().WithPath("/second").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(201));

        var definition = BuildDefinition(
            runIf: "{{stage:first.output.http_status}} not in [200, 201]");

        var document = new WorkflowDocument(
            definition,
            "/tmp/test.workflow",
            new Dictionary<string, string>());

        var catalogVersion = BuildCatalogVersion(server);

        using var httpClient = new HttpClient();
        var executor = new WorkflowExecutor(httpClient, new DynamicValueService());

        await executor.ExecuteAsync(
            document,
            catalogVersion,
            "test",
            new Dictionary<string, string>(),
            varsOverrideActive: false,
            mocked: false,
            verbose: false,
            debug: false,
            cancellationToken: CancellationToken.None);

        Assert.Single(server.LogEntries);
    }

    private static WorkflowDefinition BuildDefinition(string runIf)
    {
        return new WorkflowDefinition
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
            Stages = new List<WorkflowStageDefinition>
            {
                new()
                {
                    Name = "first",
                    Kind = WorkflowStageKind.Endpoint,
                    ApiRef = "accounts",
                    Endpoint = "/first",
                    HttpVerb = "GET",
                    ExpectedStatus = 200
                },
                new()
                {
                    Name = "second",
                    Kind = WorkflowStageKind.Endpoint,
                    ApiRef = "accounts",
                    Endpoint = "/second",
                    HttpVerb = "GET",
                    ExpectedStatus = 201,
                    RunIf = runIf
                }
            }
        };
    }

    private static ApiCatalogVersion BuildCatalogVersion(WireMockServer server)
    {
        return new ApiCatalogVersion(
            "test",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["test"] = server.Url!
            },
            new List<ApiDefinition>
            {
                new("accounts", "http://unused", null, null)
            });
    }
}
