using System.Text.Json;

using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;

namespace SphereIntegrationHub.Tests;

public sealed class WorkflowExecutionReportWriterTests
{
    [Fact]
    public async Task WriteAsync_WritesJsonAndHtmlReports()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"aos-report-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var workflowPath = Path.Combine(tempRoot, "workflow.workflow");
        File.WriteAllText(workflowPath, "version: 1.0");

        var document = new WorkflowDocument(
            new WorkflowDefinition
            {
                Id = "wf-1",
                Name = "workflow-report",
                Version = "1.0"
            },
            workflowPath,
            new Dictionary<string, string>());

        var report = new WorkflowExecutionReport
        {
            ExecutionId = "exec-1",
            WorkflowName = "workflow-report",
            WorkflowId = "wf-1",
            WorkflowVersion = "1.0",
            WorkflowPath = workflowPath,
            Environment = "dev",
            StartedAtUtc = DateTimeOffset.UtcNow,
            FinishedAtUtc = DateTimeOffset.UtcNow,
            DurationMs = 42,
            Result = "Ok",
            Output = new Dictionary<string, object?>
            {
                ["payload"] = JsonSerializer.SerializeToElement(new { id = 1 })
            }
        };
        report.Stages.Add(new WorkflowStageExecutionRecord
        {
            WorkflowName = "workflow-report",
            StageName = "create",
            StageKind = "Endpoint",
            Status = "Ok",
            StartedAtUtc = DateTimeOffset.UtcNow,
            FinishedAtUtc = DateTimeOffset.UtcNow,
            DurationMs = 10
        });

        var writer = new WorkflowExecutionReportWriter();
        var artifacts = await writer.WriteAsync(
            report,
            document,
            new WorkflowExecutionReportOptions(true, ExecutionReportFormat.Both, ExecutionHttpCaptureMode.Headers, true, true),
            CancellationToken.None);

        Assert.NotNull(artifacts.JsonReportPath);
        Assert.NotNull(artifacts.HtmlReportPath);
        Assert.True(File.Exists(artifacts.JsonReportPath!));
        Assert.True(File.Exists(artifacts.HtmlReportPath!));

        using var parsed = JsonDocument.Parse(await File.ReadAllTextAsync(artifacts.JsonReportPath!));
        Assert.Equal("Ok", parsed.RootElement.GetProperty("Result").GetString());
        var html = await File.ReadAllTextAsync(artifacts.HtmlReportPath!);
        Assert.Contains("Workflow Report", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Redactor_RedactsSensitiveHeadersAndBodies()
    {
        var headers = WorkflowExecutionRedactor.RedactHeaders(
            new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer secret",
                ["X-Trace"] = "ok"
            },
            enabled: true);

        var body = WorkflowExecutionRedactor.RedactBody("""{"password":"secret","name":"john"}""", enabled: true);

        Assert.Equal("***REDACTED***", headers!["Authorization"]);
        Assert.Equal("ok", headers["X-Trace"]);
        Assert.Contains("***REDACTED***", body, StringComparison.Ordinal);
    }
}
