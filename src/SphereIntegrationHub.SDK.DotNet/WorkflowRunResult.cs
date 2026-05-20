namespace SphereIntegrationHub.Sdk;

public sealed record WorkflowRunResult(
    IReadOnlyDictionary<string, string> Output,
    string WorkflowPath,
    string Environment,
    string CatalogVersion,
    string? CatalogPath,
    string? VarsFilePath,
    string? OutputFilePath,
    string? JsonReportPath,
    string? HtmlReportPath,
    string? ExecutionId);
