namespace SphereIntegrationHub.MCP.Models;

/// <summary>
/// MCP contract shape returned by GenerateWorkflowSkeletonTool.
/// Serializes to camelCase JSON via McpServer._jsonOptions.
/// </summary>
public sealed record WorkflowSkeletonResult(
    string Name,
    string Version,
    string Yaml,
    string? Wfvars,
    IReadOnlyList<string> AuthoringHints,
    IReadOnlyList<string> Warnings);
