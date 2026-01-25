using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;

namespace SphereIntegrationHub.Tests;

public sealed class WorkflowExecutorMockedJumpTests
{
    [Fact]
    public async Task ExecuteAsync_MockedSelfJump_Throws()
    {
        var definition = new WorkflowDefinition
        {
            Version = "1.0",
            Id = "mocked-jump",
            Name = "mocked-jump",
            Stages = new List<WorkflowStageDefinition>
            {
                new()
                {
                    Name = "self",
                    Kind = WorkflowStageKind.Endpoint,
                    ExpectedStatus = 200,
                    JumpOnStatus = new Dictionary<int, string> { [200] = "self" },
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
            "/tmp/mock-jump.workflow",
            new Dictionary<string, string>());

        var catalogVersion = new ApiCatalogVersion(
            "test",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            new List<ApiDefinition>());

        using var httpClient = new HttpClient();
        var executor = new WorkflowExecutor(httpClient, new DynamicValueService());

        await Assert.ThrowsAsync<MockedSelfJumpException>(() => executor.ExecuteAsync(
            document,
            catalogVersion,
            "test",
            new Dictionary<string, string>(),
            varsOverrideActive: false,
            mocked: true,
            verbose: false,
            debug: false,
            cancellationToken: CancellationToken.None));
    }
}
