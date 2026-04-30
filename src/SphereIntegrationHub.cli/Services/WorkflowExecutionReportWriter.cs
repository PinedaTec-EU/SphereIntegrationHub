using System.Reflection;
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
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(htmlPath, BuildHtml(report, htmlPath, json), cancellationToken);
        }

        return new WorkflowExecutionArtifacts(jsonPath, htmlPath);
    }

    private static string BuildHtml(WorkflowExecutionReport report, string htmlPath, string rawJson)
    {
        var appVersion = typeof(WorkflowExecutionReportWriter).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion?.Split('+')[0]
            ?? typeof(WorkflowExecutionReportWriter).Assembly.GetName().Version?.ToString()
            ?? string.Empty;

        var artifact = new ExecutionReportHtmlArtifact(
            Path.GetFullPath(htmlPath),
            Path.GetFileName(htmlPath),
            rawJson,
            report);

        return ExecutionReportGenerator.BuildHtml([artifact], 0, appVersion);
    }
}
