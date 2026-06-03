using System.Reflection;

namespace SphereIntegrationHub.Services;

internal static class ExecutionReportTemplateLoader
{
    private const string ReportTemplateResourceName = "SphereIntegrationHub.cli.Templates.ExecutionReport.report.html";
    private static readonly Lazy<string> ReportTemplate = new(LoadTemplate);

    internal static string LoadReportTemplate()
    {
        return ReportTemplate.Value;
    }

    private static string LoadTemplate()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(ReportTemplateResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Embedded report template '{ReportTemplateResourceName}' was not found.");
        }

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
