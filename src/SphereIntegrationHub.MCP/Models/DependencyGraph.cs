namespace SphereIntegrationHub.MCP.Models;

/// <summary>
/// A directed graph representing dependencies between endpoints
/// </summary>
public sealed record DependencyGraph
{
    public List<DependencyNode> Nodes { get; init; } = [];
    public List<DependencyEdge> Edges { get; init; } = [];
    public List<string> TopologicalOrder { get; init; } = [];
    public List<CircularDependency> Cycles { get; init; } = [];
}

public sealed record DependencyNode
{
    public required string Id { get; init; }
    public required string ApiName { get; init; }
    public required string Endpoint { get; init; }
    public required string HttpVerb { get; init; }
    public required int Level { get; init; } // Depth in the graph (0 = no dependencies)
    public List<string> RequiredFields { get; init; } = [];
    public List<string> ProvidedFields { get; init; } = [];
}

public sealed record DependencyEdge
{
    public required string From { get; init; }
    public required string To { get; init; }
    public required string Field { get; init; }
    public required double Confidence { get; init; }
    public required string Reason { get; init; }
}

public sealed record CircularDependency
{
    public required List<string> Cycle { get; init; }
    public required string Description { get; init; }
}
