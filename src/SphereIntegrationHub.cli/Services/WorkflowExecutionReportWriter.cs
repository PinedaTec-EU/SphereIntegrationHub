using System.Text;
using System.Text.Json;

using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services.Interfaces;

namespace SphereIntegrationHub.Services;

public sealed class WorkflowExecutionReportWriter : IWorkflowExecutionReportWriter
{
    public async Task<WorkflowExecutionArtifacts> WriteAsync(
        WorkflowExecutionReport report,
        WorkflowDocument document,
        WorkflowExecutionReportOptions options,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled || options.Format == ExecutionReportFormat.None)
        {
            return new WorkflowExecutionArtifacts(null, null);
        }

        var baseDirectory = Path.GetDirectoryName(document.FilePath) ?? string.Empty;
        var outputDirectory = Path.Combine(baseDirectory, "output");
        Directory.CreateDirectory(outputDirectory);
        var safeName = document.Definition.Name.Replace(' ', '-');
        var prefix = $"{safeName}.{document.Definition.Id}.{report.ExecutionId}";

        string? jsonPath = null;
        string? htmlPath = null;

        if (options.Format is ExecutionReportFormat.Json or ExecutionReportFormat.Both)
        {
            jsonPath = Path.Combine(outputDirectory, $"{prefix}.workflow.report.json");
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(jsonPath, json, cancellationToken);
        }

        if (options.Format is ExecutionReportFormat.Html or ExecutionReportFormat.Both)
        {
            htmlPath = Path.Combine(outputDirectory, $"{prefix}.workflow.report.html");
            await File.WriteAllTextAsync(htmlPath, BuildHtml(report), cancellationToken);
        }

        return new WorkflowExecutionArtifacts(jsonPath, htmlPath);
    }

    private static string BuildHtml(WorkflowExecutionReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html><head><meta charset=\"utf-8\" />");
        builder.AppendLine($"<title>Workflow Report - {report.WorkflowName}</title>");
        builder.AppendLine("""
<style>
body { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; margin: 24px; background: #fafafa; color: #111; }
h1, h2 { margin-bottom: 8px; }
table { border-collapse: collapse; width: 100%; margin-bottom: 24px; background: white; }
th, td { border: 1px solid #ddd; padding: 8px; vertical-align: top; text-align: left; }
th { background: #f0f0f0; }
.ok { color: #0a7a2f; }
.error { color: #a61b1b; }
.skipped { color: #7a5b00; }
pre { white-space: pre-wrap; word-break: break-word; margin: 0; }
</style>
</head><body>
""");
        builder.AppendLine($"<h1>Workflow Report: {System.Net.WebUtility.HtmlEncode(report.WorkflowName)}</h1>");
        builder.AppendLine("<table><tbody>");
        builder.AppendLine(Row("Execution Id", report.ExecutionId));
        builder.AppendLine(Row("Workflow Id", report.WorkflowId));
        builder.AppendLine(Row("Version", report.WorkflowVersion));
        builder.AppendLine(Row("Environment", report.Environment));
        builder.AppendLine(Row("Result", report.Result));
        builder.AppendLine(Row("Duration (ms)", report.DurationMs.ToString()));
        builder.AppendLine(Row("Output File", report.OutputFilePath ?? string.Empty));
        builder.AppendLine("</tbody></table>");

        builder.AppendLine("<h2>Metrics</h2>");
        builder.AppendLine("<table><tbody>");
        builder.AppendLine(Row("Total stages", report.Metrics.TotalStages.ToString()));
        builder.AppendLine(Row("Executed", report.Metrics.ExecutedStages.ToString()));
        builder.AppendLine(Row("Skipped", report.Metrics.SkippedStages.ToString()));
        builder.AppendLine(Row("Failed", report.Metrics.FailedStages.ToString()));
        builder.AppendLine(Row("Mocked", report.Metrics.MockedStages.ToString()));
        builder.AppendLine(Row("Retries", report.Metrics.TotalRetries.ToString()));
        builder.AppendLine("</tbody></table>");

        builder.AppendLine("<h2>Stages</h2>");
        builder.AppendLine("<table><thead><tr><th>Workflow</th><th>Stage</th><th>Status</th><th>HTTP</th><th>Jump</th><th>Retries</th><th>Duration</th><th>Error</th></tr></thead><tbody>");
        foreach (var stage in report.Stages)
        {
            builder.AppendLine("<tr>");
            builder.AppendLine(Cell(stage.WorkflowName));
            builder.AppendLine(Cell(stage.StageName));
            builder.AppendLine(Cell(stage.Status));
            builder.AppendLine(Cell(stage.HttpStatusCode?.ToString() ?? string.Empty));
            builder.AppendLine(Cell(stage.JumpTarget ?? string.Empty));
            builder.AppendLine(Cell(stage.RetryCount.ToString()));
            builder.AppendLine(Cell(stage.DurationMs.ToString()));
            builder.AppendLine(Cell(stage.ErrorMessage ?? string.Empty));
            builder.AppendLine("</tr>");
        }
        builder.AppendLine("</tbody></table>");

        builder.AppendLine("<h2>Output</h2>");
        builder.AppendLine($"<pre>{System.Net.WebUtility.HtmlEncode(JsonSerializer.Serialize(report.Output, new JsonSerializerOptions { WriteIndented = true }))}</pre>");
        builder.AppendLine("</body></html>");
        return builder.ToString();
    }

    private static string Row(string key, string value)
        => $"<tr><th>{System.Net.WebUtility.HtmlEncode(key)}</th><td>{System.Net.WebUtility.HtmlEncode(value)}</td></tr>";

    private static string Cell(string value)
        => $"<td>{System.Net.WebUtility.HtmlEncode(value)}</td>";
}
