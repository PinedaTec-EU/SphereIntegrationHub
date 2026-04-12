namespace SphereIntegrationHub.MCP.Models;

/// <summary>
/// MCP contract shape for validation tool results.
/// Serializes to camelCase JSON via McpServer._jsonOptions.
/// </summary>
public sealed record McpValidationResult(
    bool IsValid,
    IReadOnlyList<McpValidationError> Errors,
    IReadOnlyList<McpValidationWarning> Warnings);

public sealed record McpValidationError(
    string Category,
    string? Stage,
    string? Field,
    string Message,
    string? Suggestion,
    string? Location);

public sealed record McpValidationWarning(
    string Category,
    string Message,
    string? Suggestion);
