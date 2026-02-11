namespace SphereIntegrationHub.MCP.Models;

/// <summary>
/// Represents the dependencies and execution order for an endpoint
/// </summary>
public sealed record EndpointDependencies
{
    public required string Endpoint { get; init; }
    public required string HttpVerb { get; init; }
    public required string ApiName { get; init; }
    public List<RequiredField> RequiredFields { get; init; } = [];
    public List<ExecutionStep> SuggestedExecutionOrder { get; init; } = [];
}

public sealed record RequiredField
{
    public required string Field { get; init; }
    public required string Type { get; init; }
    public required string Location { get; init; } // "body", "header", "query", "path"
    public List<FieldSource> PossibleSources { get; init; } = [];
}

public sealed record FieldSource
{
    public required string Endpoint { get; init; }
    public required string HttpVerb { get; init; }
    public required string ApiName { get; init; }
    public required string ResponseField { get; init; }
    public required double Confidence { get; init; }
    public required string Reasoning { get; init; }
}

public sealed record ExecutionStep
{
    public required int Step { get; init; }
    public required string Endpoint { get; init; }
    public required string HttpVerb { get; init; }
    public required string ApiName { get; init; }
    public required string Reason { get; init; }
}
