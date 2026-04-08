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
        var prefix = $"{safeName}.{report.ExecutionId}";

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
        builder.AppendLine("<html lang=\"en\" data-theme=\"light\"><head><meta charset=\"utf-8\" />");
        builder.AppendLine("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        builder.AppendLine($"<title>Workflow Report - {System.Net.WebUtility.HtmlEncode(report.WorkflowName)}</title>");
        builder.AppendLine($$"""
<style>
:root {
  --bg:#f5f7fa;--surface:#fff;--border:#e2e8f0;--text:#1e293b;--text-muted:#64748b;--text-subtle:#94a3b8;
  --th-bg:#f8fafc;--header-bg:#0f172a;--header-text:#f1f5f9;--header-muted:#475569;
  --ok-bg:#f0fdf4;--ok-c:#15803d;--err-bg:#fff1f2;--err-c:#be123c;--skip-bg:#fefce8;--skip-c:#a16207;
  --btn-bg:#1e293b;--btn-c:#94a3b8;--btn-border:#334155;--btn-hover-bg:#334155;--btn-hover-c:#f1f5f9;
  --pre-bg:#f8fafc;--pre-border:#e2e8f0;--pre-text:#475569;
}
[data-theme="dark"] {
  --bg:#0f172a;--surface:#1e293b;--border:#334155;--text:#f1f5f9;--text-muted:#94a3b8;--text-subtle:#64748b;
  --th-bg:#1e293b;--header-bg:#020617;--header-text:#f1f5f9;--header-muted:#64748b;
  --ok-bg:#052e16;--ok-c:#4ade80;--err-bg:#4c0519;--err-c:#fda4af;--skip-bg:#422006;--skip-c:#fcd34d;
  --btn-bg:#334155;--btn-c:#94a3b8;--btn-border:#475569;--btn-hover-bg:#475569;--btn-hover-c:#f1f5f9;
  --pre-bg:#0f172a;--pre-border:#334155;--pre-text:#94a3b8;
}
*,*::before,*::after { box-sizing: border-box; }
body { margin: 0; font-family: ui-sans-serif,system-ui,-apple-system,BlinkMacSystemFont,"Segoe UI",Roboto,sans-serif; font-size: 13px; background: var(--bg); color: var(--text); transition: background .2s, color .2s; }
header { background: var(--header-bg); color: var(--header-text); padding: 12px 24px; display: flex; align-items: center; gap: 12px; }
.header-logo { width: 28px; height: 28px; flex-shrink: 0; }
.header-brand { font-size: 11px; font-weight: 700; letter-spacing: .1em; text-transform: uppercase; color: var(--header-muted); white-space: nowrap; }
.header-sep { color: #334155; font-size: 16px; }
.header-title { font-size: 15px; font-weight: 600; flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.btn { background: var(--btn-bg); color: var(--btn-c); border: 1px solid var(--btn-border); padding: 5px 11px; border-radius: 6px; cursor: pointer; font-size: 12px; font-family: inherit; transition: background .15s, color .15s; }
.btn:hover { background: var(--btn-hover-bg); color: var(--btn-hover-c); }
.btn-icon { padding: 5px 9px; font-size: 14px; }
main { padding: 24px; max-width: 1100px; }
h2 { font-size: 13px; font-weight: 700; text-transform: uppercase; letter-spacing: .06em; color: var(--text-subtle); margin: 24px 0 8px; padding-bottom: 6px; border-bottom: 1px solid var(--border); }
table { border-collapse: collapse; width: 100%; margin-bottom: 8px; background: var(--surface); border: 1px solid var(--border); border-radius: 8px; overflow: hidden; transition: background .2s; }
th, td { border-bottom: 1px solid var(--border); padding: 7px 12px; vertical-align: top; text-align: left; font-size: 12.5px; }
tr:last-child th, tr:last-child td { border-bottom: none; }
th { background: var(--th-bg); color: var(--text-muted); font-weight: 600; font-size: 11.5px; white-space: nowrap; }
.badge { display: inline-block; padding: 1px 8px; border-radius: 999px; font-size: 11px; font-weight: 700; }
.ok { background: var(--ok-bg); color: var(--ok-c); }
.error { background: var(--err-bg); color: var(--err-c); }
.skipped { background: var(--skip-bg); color: var(--skip-c); }
pre { background: var(--pre-bg); border: 1px solid var(--pre-border); border-radius: 6px; padding: 12px 14px; font-family: ui-monospace,SFMono-Regular,Menlo,monospace; font-size: 11.5px; color: var(--pre-text); white-space: pre-wrap; word-break: break-word; margin: 0; transition: background .2s, border-color .2s; }
</style>
</head>
<body>
<header>
  {{ReportBranding.HeaderLogoSvg}}
  <span class="header-brand">{{ReportBranding.HeaderTitle}}</span>
  <span class="header-sep">·</span>
  <span class="header-title">Workflow Report</span>
  <button class="btn btn-icon" id="theme-toggle" title="Toggle dark/light mode" onclick="toggleTheme()">🌙</button>
</header>
<main>
""");
        builder.AppendLine($"<h2>Summary &mdash; {System.Net.WebUtility.HtmlEncode(report.WorkflowName)}</h2>");
        builder.AppendLine("<table><tbody>");
        builder.AppendLine(Row("Execution Id", report.ExecutionId));
        builder.AppendLine(Row("Workflow Id", report.WorkflowId));
        builder.AppendLine(Row("Version", report.WorkflowVersion));
        builder.AppendLine(Row("Environment", report.Environment));
        builder.AppendLine(StatusRow("Result", report.Result));
        builder.AppendLine(Row("Duration (ms)", report.DurationMs.ToString()));
        builder.AppendLine(Row("Output File", report.OutputFilePath ?? string.Empty));
        builder.AppendLine("</tbody></table>");

        if (report.Preflight.Operations.Count > 0)
        {
            builder.AppendLine("<h2>Preflight</h2>");
            builder.AppendLine("<table><tbody>");
            builder.AppendLine(Row("Operations", report.Preflight.Operations.Count.ToString()));
            builder.AppendLine(Row("Retries", report.Preflight.TotalRetries.ToString()));
            builder.AppendLine(Row("Duration (ms)", report.Preflight.DurationMs.ToString()));
            builder.AppendLine("</tbody></table>");

            builder.AppendLine("<table><thead><tr><th>Type</th><th>Definition</th><th>Status</th><th>Target</th><th>Retries</th><th>Duration (ms)</th><th>Message</th></tr></thead><tbody>");
            foreach (var operation in report.Preflight.Operations)
            {
                builder.AppendLine("<tr>");
                builder.AppendLine(Cell(operation.OperationType));
                builder.AppendLine(Cell(operation.DefinitionName));
                builder.AppendLine(StatusCell(operation.Status));
                builder.AppendLine(Cell(operation.Target ?? string.Empty));
                builder.AppendLine(Cell(operation.RetryCount.ToString()));
                builder.AppendLine(Cell(operation.DurationMs.ToString()));
                builder.AppendLine(Cell(operation.Message ?? string.Empty));
                builder.AppendLine("</tr>");
            }
            builder.AppendLine("</tbody></table>");
        }

        builder.AppendLine("<h2>Metrics</h2>");
        builder.AppendLine("<table><tbody>");
        builder.AppendLine(Row("Total stages", report.Metrics.TotalStages.ToString()));
        builder.AppendLine(Row("Executed", report.Metrics.ExecutedStages.ToString()));
        builder.AppendLine(Row("Skipped", report.Metrics.SkippedStages.ToString()));
        builder.AppendLine(Row("Failed", report.Metrics.FailedStages.ToString()));
        builder.AppendLine(Row("Mocked", report.Metrics.MockedStages.ToString()));
        builder.AppendLine(Row("Retries", report.Metrics.TotalRetries.ToString()));
        builder.AppendLine(Row("Preflight retries", report.Metrics.PreflightRetries.ToString()));
        builder.AppendLine("</tbody></table>");

        builder.AppendLine("<h2>Stages</h2>");
        builder.AppendLine("<table><thead><tr><th>Workflow</th><th>Stage</th><th>Status</th><th>HTTP</th><th>Jump</th><th>Retries</th><th>Duration (ms)</th><th>Error</th></tr></thead><tbody>");
        foreach (var stage in report.Stages)
        {
            builder.AppendLine("<tr>");
            builder.AppendLine(Cell(stage.WorkflowName));
            builder.AppendLine(Cell(stage.StageName));
            builder.AppendLine(StatusCell(stage.Status));
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
        builder.AppendLine("""
</main>
<script>
function applyTheme(dark) {
  document.documentElement.dataset.theme = dark ? 'dark' : 'light';
  document.getElementById('theme-toggle').textContent = dark ? '☀️' : '🌙';
}
function toggleTheme() {
  const dark = document.documentElement.dataset.theme !== 'dark';
  localStorage.setItem('sphere-theme', dark ? 'dark' : 'light');
  applyTheme(dark);
}
(function() {
  const saved = localStorage.getItem('sphere-theme');
  const prefersDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
  applyTheme(saved ? saved === 'dark' : prefersDark);
})();
</script>
</body></html>
""");
        return builder.ToString();
    }

    private static string Row(string key, string value)
        => $"<tr><th>{System.Net.WebUtility.HtmlEncode(key)}</th><td>{System.Net.WebUtility.HtmlEncode(value)}</td></tr>";

    private static string StatusRow(string key, string value)
    {
        var cls = value?.ToLowerInvariant() switch { "ok" => "ok", "error" => "error", "skipped" => "skipped", _ => "" };
        var cell = string.IsNullOrEmpty(cls)
            ? System.Net.WebUtility.HtmlEncode(value)
            : $"<span class=\"badge {cls}\">{System.Net.WebUtility.HtmlEncode(value)}</span>";
        return $"<tr><th>{System.Net.WebUtility.HtmlEncode(key)}</th><td>{cell}</td></tr>";
    }

    private static string Cell(string value)
        => $"<td>{System.Net.WebUtility.HtmlEncode(value)}</td>";

    private static string StatusCell(string value)
    {
        var cls = value?.ToLowerInvariant() switch { "ok" => "ok", "error" => "error", "skipped" => "skipped", _ => "" };
        return string.IsNullOrEmpty(cls)
            ? $"<td>{System.Net.WebUtility.HtmlEncode(value)}</td>"
            : $"<td><span class=\"badge {cls}\">{System.Net.WebUtility.HtmlEncode(value)}</span></td>";
    }
}
