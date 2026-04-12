namespace SphereIntegrationHub.MCP.Models;

/// <summary>
/// MCP contract shape returned by GenerateEndpointStageTool.
/// Serializes to camelCase JSON via McpServer._jsonOptions.
/// </summary>
public sealed record GenerateStageResult(
    string StageName,
    string Yaml,
    string Source);
