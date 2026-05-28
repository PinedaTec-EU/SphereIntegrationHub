using System.Text.Json;

using SphereIntegrationHub.Services;

namespace SphereIntegrationHub.Tests;

public sealed class WorkflowExecutionSnapshotServiceTests
{
    [Fact]
    public async Task CreateAsync_WritesStableSnapshot()
    {
        var tempRoot = CreateTempRoot();
        var reportPath = Path.Combine(tempRoot, "output", "workflow.exec-1.workflow.report.json");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(CreateReport("exec-1", "Ok", "customer-1")));

        var snapshotPath = Path.Combine(tempRoot, "snapshots", "baseline.json");
        var service = new WorkflowExecutionSnapshotService();

        var result = await service.CreateAsync(reportPath, snapshotPath, "happy-path", CancellationToken.None);

        Assert.Equal(snapshotPath, result.SnapshotPath);
        Assert.True(File.Exists(snapshotPath));
        using var parsed = JsonDocument.Parse(await File.ReadAllTextAsync(snapshotPath));
        Assert.Equal("happy-path", parsed.RootElement.GetProperty("Name").GetString());
        Assert.Equal("customer-1", parsed.RootElement
            .GetProperty("Baseline")
            .GetProperty("output")
            .GetProperty("customerId")
            .GetString());
    }

    [Fact]
    public async Task CompareAsync_WhenReportMatchesSnapshot_ReturnsMatch()
    {
        var tempRoot = CreateTempRoot();
        var reportPath = Path.Combine(tempRoot, "output", "workflow.exec-1.workflow.report.json");
        var nextReportPath = Path.Combine(tempRoot, "output", "workflow.exec-2.workflow.report.json");
        var snapshotPath = Path.Combine(tempRoot, "snapshots", "baseline.json");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(CreateReport("exec-1", "Ok", "customer-1")));
        await File.WriteAllTextAsync(nextReportPath, JsonSerializer.Serialize(CreateReport("exec-2", "Ok", "customer-1")));
        var service = new WorkflowExecutionSnapshotService();
        await service.CreateAsync(reportPath, snapshotPath, "happy-path", CancellationToken.None);

        var result = await service.CompareAsync(nextReportPath, snapshotPath, CancellationToken.None);

        Assert.True(result.Matches);
        Assert.Empty(result.Differences);
    }

    [Fact]
    public async Task CompareAsync_WhenOutputDiffers_ReturnsPathAndValues()
    {
        var tempRoot = CreateTempRoot();
        var reportPath = Path.Combine(tempRoot, "output", "workflow.exec-1.workflow.report.json");
        var nextReportPath = Path.Combine(tempRoot, "output", "workflow.exec-2.workflow.report.json");
        var snapshotPath = Path.Combine(tempRoot, "snapshots", "baseline.json");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(CreateReport("exec-1", "Ok", "customer-1")));
        await File.WriteAllTextAsync(nextReportPath, JsonSerializer.Serialize(CreateReport("exec-2", "Ok", "customer-2")));
        var service = new WorkflowExecutionSnapshotService();
        await service.CreateAsync(reportPath, snapshotPath, "happy-path", CancellationToken.None);

        var result = await service.CompareAsync(nextReportPath, snapshotPath, CancellationToken.None);

        Assert.False(result.Matches);
        var difference = Assert.Single(result.Differences, item => item.Path == "$.output.customerId");
        Assert.Equal("customer-1", difference.Expected);
        Assert.Equal("customer-2", difference.Actual);
    }

    private static WorkflowExecutionReport CreateReport(string executionId, string result, string customerId)
    {
        var report = new WorkflowExecutionReport
        {
            ExecutionId = executionId,
            WorkflowName = "customer-flow",
            WorkflowId = "wf-customer",
            WorkflowVersion = "1.0",
            Environment = "local",
            StartedAtUtc = DateTimeOffset.Parse("2026-05-28T00:00:00Z"),
            FinishedAtUtc = DateTimeOffset.Parse("2026-05-28T00:00:01Z"),
            DurationMs = 1000,
            Result = result,
            Output = new Dictionary<string, object?>
            {
                ["customerId"] = customerId
            },
            Inputs = new Dictionary<string, string>
            {
                ["tenant"] = "acme"
            }
        };

        report.Metrics.TotalStages = 1;
        report.Metrics.ExecutedStages = 1;
        report.Stages.Add(new WorkflowStageExecutionRecord
        {
            WorkflowName = "customer-flow",
            StageName = "create-customer",
            StageKind = "Endpoint",
            Status = "Ok",
            StartedAtUtc = report.StartedAtUtc,
            FinishedAtUtc = report.FinishedAtUtc,
            DurationMs = 1000,
            HttpStatusCode = 201,
            Output = new Dictionary<string, object?>
            {
                ["customerId"] = customerId
            }
        });

        return report;
    }

    private static string CreateTempRoot()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"sih-snapshot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        return tempRoot;
    }
}
