namespace SphereIntegrationHub.MCP.Models;

/// <summary>
/// A suggested workflow generated from a natural language goal
/// </summary>
public sealed record WorkflowSuggestion
{
    public required string Goal { get; init; }
    public required string WorkflowName { get; init; }
    public required string WorkflowYaml { get; init; }
    public List<SuggestionStage> Stages { get; init; } = [];
    public List<string> RequiredApis { get; init; } = [];
    public List<string> Assumptions { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public required string Confidence { get; init; } // "high", "medium", "low"
}

public sealed record SuggestionStage
{
    public required string StageName { get; init; }
    public required string ApiName { get; init; }
    public required string Endpoint { get; init; }
    public required string HttpVerb { get; init; }
    public required string Purpose { get; init; }
    public List<string> DataBindings { get; init; } = [];
}
