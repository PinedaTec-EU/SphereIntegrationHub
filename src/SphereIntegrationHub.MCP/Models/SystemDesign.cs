namespace SphereIntegrationHub.MCP.Models;

/// <summary>
/// Complete system design generated from natural language description
/// </summary>
public sealed record SystemDesign
{
    public List<WorkflowDesign> Workflows { get; init; } = [];
    public List<WorkflowDependency> Dependencies { get; init; } = [];
    public List<TestScenario> TestScenarios { get; init; } = [];
    public Dictionary<string, List<string>> ApiUsage { get; init; } = [];
    public required string EstimatedComplexity { get; init; }
    public required string EstimatedExecutionTime { get; init; }
}

public sealed record WorkflowDesign
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required string Yaml { get; init; }
    public required int Stages { get; init; }
    public required string Description { get; init; }
}

public sealed record WorkflowDependency
{
    public required string From { get; init; }
    public required string To { get; init; }
    public required string Reason { get; init; }
}

public sealed record TestScenario
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public List<StageMock>? Mocks { get; init; }
    public string? ExpectedBehavior { get; init; }
    public string? MockFile { get; init; }
}

public sealed record StageMock
{
    public required string Stage { get; init; }
    public required int Status { get; init; }
    public required string Payload { get; init; }
}
