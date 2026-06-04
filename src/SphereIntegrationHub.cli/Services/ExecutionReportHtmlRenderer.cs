using System.Text.Json;

namespace SphereIntegrationHub.Services;

internal sealed class ExecutionReportHtmlRenderer
{
    private static readonly WorkflowExecutionSnapshotService SnapshotService = new();

    public string Render(
        IReadOnlyList<ExecutionReportHtmlArtifact> reports,
        int initialReportIndex,
        string appVersion,
        IReadOnlyList<ExecutionSnapshotHtmlArtifact>? snapshots = null)
    {
        var template = ExecutionReportTemplateLoader.LoadReportTemplate();
        var placeholders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["{{FAVICON_DATA_URI}}"] = $"data:image/svg+xml,{Uri.EscapeDataString(ReportBranding.FaviconSvg)}",
            ["{{HEADER_LOGO_SVG}}"] = ReportBranding.HeaderLogoSvg.Replace("class=\"header-logo\"", "class=\"banner-logo\"", StringComparison.Ordinal),
            ["{{HEADER_TITLE}}"] = ReportBranding.HeaderTitle,
            ["{{APP_VERSION}}"] = appVersion,
            ["{{APP_VERSION_JSON}}"] = JsonSerializer.Serialize(appVersion),
            ["{{INITIAL_REPORT_INDEX}}"] = initialReportIndex.ToString(),
            ["{{REPORTS_JSON}}"] = BuildReportsJson(reports),
            ["{{SNAPSHOTS_JSON}}"] = BuildSnapshotsJson(snapshots)
        };

        return placeholders.Aggregate(
            template,
            static (current, placeholder) => current.Replace(placeholder.Key, placeholder.Value, StringComparison.Ordinal));
    }

    private static string BuildReportsJson(IReadOnlyList<ExecutionReportHtmlArtifact> reports)
    {
        return JsonSerializer.Serialize(reports.Select(report => new
        {
            path = report.Path,
            fileName = report.FileName,
            executionId = report.Report.ExecutionId,
            workflowName = report.Report.WorkflowName,
            result = report.Report.Result,
            startedAtUtc = report.Report.StartedAtUtc,
            toolVersion = report.Report.ToolVersion,
            snapshotJson = SnapshotService.CreateSnapshot(report.Report),
            json = JsonSerializer.Deserialize<JsonElement>(report.RawJson)
        }));
    }

    private static string BuildSnapshotsJson(IReadOnlyList<ExecutionSnapshotHtmlArtifact>? snapshots)
    {
        return JsonSerializer.Serialize((snapshots ?? Array.Empty<ExecutionSnapshotHtmlArtifact>()).Select(snapshot => new
        {
            path = snapshot.Path,
            fileName = snapshot.FileName,
            name = snapshot.Snapshot.Name,
            sourceExecutionId = snapshot.Snapshot.SourceExecutionId,
            workflowName = snapshot.Snapshot.WorkflowName,
            workflowVersion = snapshot.Snapshot.WorkflowVersion,
            environment = snapshot.Snapshot.Environment,
            json = JsonSerializer.Deserialize<JsonElement>(snapshot.RawJson)
        }));
    }
}
