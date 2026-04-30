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
            },
            Preflight = new WorkflowPreflightReport
            {
                TotalRetries = 1,
                DurationMs = 120
            }
        };
        report.Preflight.Operations.Add(new WorkflowPreflightOperationRecord
        {
            OperationType = "HealthCheck",
            DefinitionName = "accounts",
            Status = "Ok",
            Target = "https://localhost/health",
            RetryCount = 1,
            DurationMs = 120
        });
        report.Stages.Add(new WorkflowStageExecutionRecord
        {
            WorkflowName = "workflow-report",
            StageName = "create",
            StageKind = "Endpoint",
            Status = "Ok",
            ForEachExecutionMode = "Parallel",
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
        Assert.EndsWith($"{Path.DirectorySeparatorChar}workflow-report.exec-1.workflow.report.json", artifacts.JsonReportPath, StringComparison.Ordinal);
        Assert.EndsWith($"{Path.DirectorySeparatorChar}workflow-report.exec-1.workflow.report.html", artifacts.HtmlReportPath, StringComparison.Ordinal);

        using var parsed = JsonDocument.Parse(await File.ReadAllTextAsync(artifacts.JsonReportPath!));
        Assert.Equal("Ok", parsed.RootElement.GetProperty("Result").GetString());
        Assert.Equal(1, parsed.RootElement.GetProperty("Preflight").GetProperty("TotalRetries").GetInt32());
        Assert.Equal("Parallel", parsed.RootElement.GetProperty("Stages")[0].GetProperty("ForEachExecutionMode").GetString());
        var html = await File.ReadAllTextAsync(artifacts.HtmlReportPath!);
        Assert.Contains("Workflow Trace", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("trace-container", html, StringComparison.Ordinal);
        Assert.Contains("report-picker", html, StringComparison.Ordinal);
        Assert.Contains("workflow-report", html, StringComparison.Ordinal);
        Assert.Contains("create", html, StringComparison.Ordinal);
        Assert.Contains("Sphere Integration Hub (SIH)", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Sphere Integration Hub icon\"", html, StringComparison.Ordinal);
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
