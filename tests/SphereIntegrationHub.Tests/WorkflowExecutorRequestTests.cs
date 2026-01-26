using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;

using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace SphereIntegrationHub.Tests;

public sealed class WorkflowExecutorRequestTests
{
    [Fact]
    public async Task ExecuteAsync_SendsHeadersAndJsonBody()
    {
        using WireMockServer server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/api/accounts").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"ok\":true}"));

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
            Stages = new List<WorkflowStageDefinition>
            {
                new()
                {
                    Name = "create-account",
                    Kind = WorkflowStageKinds.Endpoint,
                    ApiRef = "accounts",
                    Endpoint = "/api/accounts",
                    HttpVerb = "POST",
                    ExpectedStatus = 201,
                    Headers = new Dictionary<string, string>
                    {
                        ["Content-Type"] = "application/json-patch+json",
                        ["Authorization"] = "Bearer token",
                        ["X-Trace"] = "trace-1"
                    },
                    Body = "{\"name\":\"test\"}"
                }
            }
        };

        var document = new WorkflowDocument(
            definition,
            "/tmp/test.workflow",
            new Dictionary<string, string>());

        var catalogVersion = new ApiCatalogVersion(
            "test",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["test"] = server.Url!
            },
            new List<ApiDefinition>
            {
                new("accounts", "http://unused", null, null)
            });

        using var httpClient = new HttpClient();
        var executor = new WorkflowExecutor(httpClient, new DynamicValueService(), TestStagePlugins.CreateRegistry());

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

        var entry = Assert.Single(server.LogEntries);
        var request = entry.RequestMessage!;

        Assert.True(request.Headers!.TryGetValue("Content-Type", out var contentTypeValues));
        Assert.Contains(contentTypeValues, value =>
            value.StartsWith("application/json-patch+json", StringComparison.OrdinalIgnoreCase));
        Assert.True(request.Headers.TryGetValue("X-Trace", out var traceValues));
        Assert.Contains("trace-1", traceValues);
        Assert.True(request.Headers.TryGetValue("Authorization", out var authValues));
        Assert.Contains("Bearer token", authValues);
        Assert.Equal("{\"name\":\"test\"}", request.Body?.ToString());
    }
}
