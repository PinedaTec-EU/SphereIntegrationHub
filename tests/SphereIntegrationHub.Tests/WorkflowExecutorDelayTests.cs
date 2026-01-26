using System.Diagnostics;

using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;

namespace SphereIntegrationHub.Tests;

public sealed class WorkflowExecutorDelayTests
{
    [Fact]
    public async Task ExecuteAsync_DelaysStageExecution()
    {
        var definition = new WorkflowDefinition
        {
            Version = "3.11",
            Id = "test-delay",
            Name = "test-delay",
            Stages = new List<WorkflowStageDefinition>
            {
                new()
                {
                    Name = "delayed",
                    Kind = WorkflowStageKinds.Endpoint,
                    ExpectedStatus = 200,
                    DelaySeconds = 1,
                    Mock = new WorkflowStageMockDefinition
                    {
                        Status = 200,
                        Payload = "{}"
                    }
                }
            }
        };

        var document = new WorkflowDocument(
            definition,
            "/tmp/test-delay.workflow",
            new Dictionary<string, string>());

        var catalogVersion = new ApiCatalogVersion(
            "test",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            new List<ApiDefinition>());

        using var httpClient = new HttpClient();
        var executor = new WorkflowExecutor(httpClient, new DynamicValueService(), TestStagePlugins.CreateRegistry());

        var stopwatch = Stopwatch.StartNew();
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
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed >= TimeSpan.FromSeconds(1));
    }
}
