namespace SphereIntegrationHub.MCP.Models;

/// <summary>
/// Result of workflow or stage validation
/// </summary>
public sealed record ValidationResult
{
    public required bool Valid { get; init; }
    public List<ValidationError> Errors { get; init; } = [];
    public List<ValidationWarning> Warnings { get; init; } = [];
}

public sealed record ValidationError
{
    public required string Category { get; init; }
    public string? Stage { get; init; }
    public string? Field { get; init; }
    public required string Message { get; init; }
    public string? Suggestion { get; init; }
    public string? Location { get; init; }
}

public sealed record ValidationWarning
{
    public required string Category { get; init; }
    public required string Message { get; init; }
    public string? Suggestion { get; init; }
}
