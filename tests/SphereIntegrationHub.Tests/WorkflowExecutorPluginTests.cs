using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;

using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace SphereIntegrationHub.Tests;

public sealed class WorkflowExecutorPluginTests
{
    [Fact]
    public async Task ExecuteAsync_UsesHttpPluginConfigBlock()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/api/accounts").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"items\":[{\"id\":\"acc-1\"}]}"));

        var definition = new WorkflowDefinition
        {
            Version = "1.0",
            Id = "wf-http-plugin",
            Name = "wf-http-plugin",
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
                    Name = "list-accounts",
                    Kind = WorkflowStageKind.Http,
                    ExpectedStatus = 200,
                    Config = new Dictionary<string, object?>
                    {
                        ["apiRef"] = "accounts",
                        ["endpoint"] = "/api/accounts",
                        ["httpVerb"] = "GET"
                    },
                    Output = new Dictionary<string, string>
                    {
                        ["payload"] = "{{response.body}}"
                    }
                }
            },
            EndStage = new WorkflowEndStage
            {
                Output = new Dictionary<string, string>
                {
                    ["payload"] = "{{stage:list-accounts.output.payload}}"
                }
            }
        };

        var document = new WorkflowDocument(definition, "/tmp/sample.workflow", new Dictionary<string, string>());
        var catalogVersion = new ApiCatalogVersion
        {
            Version = "1.0",
            Definitions = new List<ApiDefinition>
            {
                new()
                {
                    Name = "accounts",
                    SwaggerUrl = "http://unused",
                    BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["test"] = server.Url!
                    }
                }
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

        Assert.Contains("\"acc-1\"", result.Output["payload"], StringComparison.Ordinal);
    }
}
