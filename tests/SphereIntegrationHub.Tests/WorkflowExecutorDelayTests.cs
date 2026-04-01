using System.Diagnostics;

using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;
using SphereIntegrationHub.Services.Interfaces;

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
                    Kind = WorkflowStageKind.Endpoint,
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

        var catalogVersion = new ApiCatalogVersion
        {
            Version = "test",
            BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Definitions = new List<ApiDefinition>()
        };

        using var httpClient = new HttpClient();
        var executor = new WorkflowExecutor(httpClient, new DynamicValueService());

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

    [Fact]
    public async Task ExecuteAsync_DelayIsLoggedUnconditionally()
    {
        var definition = new WorkflowDefinition
        {
            Version = "3.11",
            Id = "test-delay-log",
            Name = "test-delay-log",
            Stages = new List<WorkflowStageDefinition>
            {
                new()
                {
                    Name = "delayed",
                    Kind = WorkflowStageKind.Endpoint,
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
            "/tmp/test-delay-log.workflow",
            new Dictionary<string, string>());

        var catalogVersion = new ApiCatalogVersion
        {
            Version = "test",
            BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Definitions = new List<ApiDefinition>()
        };

        var logger = new TestExecutionLogger();
        using var httpClient = new HttpClient();
        var executor = new WorkflowExecutor(httpClient, new DynamicValueService(), logger: logger);

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

        Assert.Contains(logger.Infos, m => m.Contains("delay: 1s.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_DelaySecondsIsRecordedInStageExecutionRecord()
    {
        var definition = new WorkflowDefinition
        {
            Version = "3.11",
            Id = "test-delay-record",
            Name = "test-delay-record",
            Stages = new List<WorkflowStageDefinition>
            {
                new()
                {
                    Name = "delayed",
                    Kind = WorkflowStageKind.Endpoint,
                    ExpectedStatus = 200,
                    DelaySeconds = 5,
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
            "/tmp/test-delay-record.workflow",
            new Dictionary<string, string>());

        var catalogVersion = new ApiCatalogVersion
        {
            Version = "test",
            BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
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
        var stageRecord = Assert.Single(reportWriter.CapturedReport.Stages);
        Assert.Equal(5, stageRecord.DelaySeconds);
    }

    private sealed class TestExecutionLogger : IExecutionLogger
    {
        public List<string> Infos { get; } = new();
        public List<string> Errors { get; } = new();

        public void Info(string message) => Infos.Add(message);

        public void Error(string message) => Errors.Add(message);
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
