using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;
using SphereIntegrationHub.Services.Interfaces;

using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace SphereIntegrationHub.Tests;

public sealed class WorkflowExecutorWorkflowStageTests
{
    [Fact]
    public async Task ExecuteAsync_RecordsNestedWorkflowStagesInExecutionReport()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"sih-nested-report-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var parentPath = Path.Combine(tempRoot, "parent.workflow");
        var childPath = Path.Combine(tempRoot, "child.workflow");

        File.WriteAllText(childPath, """
version: "1.0"
id: "child"
name: "child"
output: true
input:
  - name: "tenantId"
    type: "Text"
    required: true
stages:
  - name: "seed-child"
    kind: "Endpoint"
    mock:
      status: 200
      payload: "{\"seeded\":true}"
    output:
      seeded: "true"
endStage:
  output:
    childTenantId: "{{input.tenantId}}"
    childSeeded: "{{stage:seed-child.output.seeded}}"
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
                        WorkflowRef = "child",
                        Inputs = new Dictionary<string, object?>
                        {
                            ["tenantId"] = "tenant-42"
                        }
                    }
                }
            };

            var document = new WorkflowDocument(definition, parentPath, new Dictionary<string, string>());
            var catalogVersion = new ApiCatalogVersion
            {
                Version = "test",
                Definitions = new List<ApiDefinition>()
            };

            var reportWriter = new TestReportWriter();
            using var httpClient = new HttpClient();
            var executor = new WorkflowExecutor(
                httpClient,
                new DynamicValueService(),
                reportWriter: reportWriter,
                reportOptions: new WorkflowExecutionReportOptions(true, ExecutionReportFormat.None, ExecutionHttpCaptureMode.None, false, false));

            await executor.ExecuteAsync(
                document,
                catalogVersion,
                "test",
                new Dictionary<string, string>(),
                varsOverrideActive: false,
                mocked: true,
                verbose: false,
                debug: false,
                cancellationToken: CancellationToken.None);

            Assert.NotNull(reportWriter.CapturedReport);
            Assert.Equal(2, reportWriter.CapturedReport!.Stages.Count);

            var parentStage = reportWriter.CapturedReport.Stages[0];
            var childStage = reportWriter.CapturedReport.Stages[1];

            Assert.Equal("run-child", parentStage.StageName);
            Assert.Equal("Workflow", parentStage.StageKind);
            Assert.Equal(0, parentStage.Depth);
            Assert.Equal("tenant-42", parentStage.WorkflowInputs["tenantId"]);
            Assert.Equal("tenant-42", parentStage.WorkflowOutput["childTenantId"]);
            Assert.Equal("True", parentStage.WorkflowOutput["childSeeded"]?.ToString());
            Assert.Equal("Ok", parentStage.WorkflowResult["status"]);

            Assert.Equal("seed-child", childStage.StageName);
            Assert.Equal("Endpoint", childStage.StageKind);
            Assert.Equal(1, childStage.Depth);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

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
                        Inputs = new Dictionary<string, object?>
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

    [Fact]
    public async Task ExecuteAsync_AllowsStructuredInlineWorkflowStageInputs()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"sih-structured-stage-inputs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var parentPath = Path.Combine(tempRoot, "parent.workflow");
        var childPath = Path.Combine(tempRoot, "child.workflow");

        File.WriteAllText(childPath, """
version: "1.0"
id: "child"
name: "child"
output: true
input:
  - name: "targets"
    type: "Array"
    required: true
  - name: "metadata"
    type: "Object"
    required: true
endStage:
  output:
    firstTarget: "{{input.targets.0}}"
    secondTarget: "{{input.targets.1}}"
    source: "{{input.metadata.source}}"
    nestedTag: "{{input.metadata.tags.1}}"
""");

        File.WriteAllText(parentPath, """
version: "1.0"
id: "parent"
name: "parent"
output: true
input:
  - name: "tenant"
    type: "Text"
    required: true
references:
  workflows:
    - name: "child"
      path: "./child.workflow"
stages:
  - name: "run-child"
    kind: "Workflow"
    workflowRef: "child"
    inputs:
      targets: ["TRG01", "{{input.tenant}}"]
      metadata:
        source: "seed"
        tags: ["fixed", "{{input.tenant}}"]
endStage:
  output:
    firstTarget: "{{workflow:run-child.output.firstTarget}}"
    secondTarget: "{{workflow:run-child.output.secondTarget}}"
    source: "{{workflow:run-child.output.source}}"
    nestedTag: "{{workflow:run-child.output.nestedTag}}"
""");

        try
        {
            using var httpClient = new HttpClient();
            var executor = new WorkflowExecutor(httpClient, new DynamicValueService());
            var loader = new WorkflowLoader();
            var document = loader.Load(parentPath);

            var result = await executor.ExecuteAsync(
                document,
                new ApiCatalogVersion
                {
                    Version = "test",
                    Definitions = new List<ApiDefinition>()
                },
                "test",
                new Dictionary<string, string>
                {
                    ["tenant"] = "TEN01"
                },
                varsOverrideActive: false,
                mocked: false,
                verbose: false,
                debug: false,
                cancellationToken: CancellationToken.None);

            Assert.Equal("TRG01", result.Output["firstTarget"]);
            Assert.Equal("TEN01", result.Output["secondTarget"]);
            Assert.Equal("seed", result.Output["source"]);
            Assert.Equal("TEN01", result.Output["nestedTag"]);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private sealed class TestReportWriter : IWorkflowExecutionReportWriter
    {
        public WorkflowExecutionReport? CapturedReport { get; private set; }

        public Task<WorkflowExecutionArtifacts> WriteAsync(
            WorkflowExecutionReport report,
            WorkflowDocument document,
            WorkflowExecutionReportOptions options,
            CancellationToken cancellationToken)
        {
            CapturedReport = report;
            return Task.FromResult(new WorkflowExecutionArtifacts(null, null));
        }
    }
}
