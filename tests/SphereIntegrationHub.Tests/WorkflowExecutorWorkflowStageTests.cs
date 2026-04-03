using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;

using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace SphereIntegrationHub.Tests;

public sealed class WorkflowExecutorWorkflowStageTests
{
    [Fact]
    public async Task ExecuteAsync_PropagatesNestedWorkflowFailuresToParent()
    {
        using WireMockServer server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/api/fail").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(500)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"error\":\"boom\"}"));

        var tempRoot = Path.Combine(Path.GetTempPath(), $"sih-nested-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var parentPath = Path.Combine(tempRoot, "parent.workflow");
        var childPath = Path.Combine(tempRoot, "child.workflow");

        File.WriteAllText(childPath, """
version: "1.0"
id: "child"
name: "child"
references:
  apis:
    - name: "accounts"
      definition: "accounts"
stages:
  - name: "fail-call"
    kind: "Endpoint"
    apiRef: "accounts"
    endpoint: "/api/fail"
    httpVerb: "GET"
    expectedStatus: 200
""");

        try
        {
            var definition = new WorkflowDefinition
            {
                Version = "1.0",
                Id = "parent",
                Name = "parent",
                References = new WorkflowReference
                {
                    Workflows = new List<WorkflowReferenceItem>
                    {
                        new() { Name = "child", Path = "./child.workflow" }
                    }
                },
                Stages = new List<WorkflowStageDefinition>
                {
                    new()
                    {
                        Name = "run-child",
                        Kind = WorkflowStageKind.Workflow,
                        WorkflowRef = "child"
                    }
                }
            };

            var document = new WorkflowDocument(definition, parentPath, new Dictionary<string, string>());
            var catalogVersion = new ApiCatalogVersion
            {
                Version = "test",
                Definitions = new List<ApiDefinition>
                {
                    new ApiDefinition { Name = "accounts", SwaggerUrl = "http://unused", BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["test"] = server.Url! } }
                }
            };

            using var httpClient = new HttpClient();
            var executor = new WorkflowExecutor(httpClient, new DynamicValueService());

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(
                document,
                catalogVersion,
                "test",
                new Dictionary<string, string>(),
                varsOverrideActive: false,
                mocked: false,
                verbose: false,
                debug: false,
                cancellationToken: CancellationToken.None));

            Assert.Contains("Nested workflow 'child' failed", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_AggregatesForEachWorkflowResults()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"sih-foreach-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var parentPath = Path.Combine(tempRoot, "parent.workflow");
        var childPath = Path.Combine(tempRoot, "child.workflow");

        File.WriteAllText(childPath, """
version: "1.0"
id: "child"
name: "child"
output: true
input:
  - name: "id"
    type: "Text"
    required: true
endStage:
  output:
    id: "{{input.id}}"
""");

        try
        {
            var definition = new WorkflowDefinition
            {
                Version = "1.0",
                Id = "parent",
                Name = "parent",
                Output = true,
                Input = new List<WorkflowInputDefinition>
                {
                    new() { Name = "items", Type = RandomValueType.Array, Required = true }
                },
                References = new WorkflowReference
                {
                    Workflows = new List<WorkflowReferenceItem>
                    {
                        new() { Name = "child", Path = "./child.workflow" }
                    }
                },
                Stages = new List<WorkflowStageDefinition>
                {
                    new()
                    {
                        Name = "run-child",
                        Kind = WorkflowStageKind.Workflow,
                        WorkflowRef = "child",
                        ForEach = "{{input.items}}",
                        Inputs = new Dictionary<string, string>
                        {
                            ["id"] = "{{context:item.id}}"
                        }
                    }
                },
                EndStage = new WorkflowEndStage
                {
                    Output = new Dictionary<string, string>
                    {
                        ["foreach_count"] = "{{stage:run-child.workflow.output.foreach_count}}",
                        ["foreach_success_count"] = "{{stage:run-child.workflow.output.foreach_success_count}}",
                        ["foreach_failed_count"] = "{{stage:run-child.workflow.output.foreach_failed_count}}",
                        ["foreach_results"] = "{{stage:run-child.workflow.output.foreach_results}}"
                    }
                }
            };

            var document = new WorkflowDocument(definition, parentPath, new Dictionary<string, string>());
            var catalogVersion = new ApiCatalogVersion
            {
                Version = "test",
                Definitions = new List<ApiDefinition>()
            };

            using var httpClient = new HttpClient();
            var executor = new WorkflowExecutor(httpClient, new DynamicValueService());

            var result = await executor.ExecuteAsync(
                document,
                catalogVersion,
                "test",
                new Dictionary<string, string>
                {
                    ["items"] = """[{"id":"a-1"},{"id":"a-2"}]"""
                },
                varsOverrideActive: false,
                mocked: false,
                verbose: false,
                debug: false,
                cancellationToken: CancellationToken.None);

            Assert.Equal("2", result.Output["foreach_count"]);
            Assert.Equal("2", result.Output["foreach_success_count"]);
            Assert.Equal("0", result.Output["foreach_failed_count"]);
            Assert.Contains("\"status\":\"Ok\"", result.Output["foreach_results"], StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }
}
