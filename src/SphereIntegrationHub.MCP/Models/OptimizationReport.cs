namespace SphereIntegrationHub.MCP.Models;

/// <summary>
/// Optimization suggestions for a workflow
/// </summary>
public sealed record OptimizationReport
{
    public required string Workflow { get; init; }
    public required WorkflowMetrics CurrentMetrics { get; init; }
    public List<Optimization> Optimizations { get; init; } = [];
    public required WorkflowMetrics ProjectedMetrics { get; init; }
}

public sealed record WorkflowMetrics
{
    public required int Stages { get; init; }
    public required string EstimatedDuration { get; init; }
    public required int HttpCalls { get; init; }
    public int SequentialCalls { get; init; }
    public int ParallelizableCalls { get; init; }
    public string? ImprovementSummary { get; init; }
}

public sealed record Optimization
{
    public required string Type { get; init; } // "parallelization", "redundancy", "resilience", "caching", "batching"
    public required string Priority { get; init; } // "high", "medium", "low"
    public List<string>? Stages { get; init; }
    public string? Stage { get; init; }
    public required string Reason { get; init; }
    public required OptimizationImpact Impact { get; init; }
    public required object Implementation { get; init; } // Can be string or complex object
}

public sealed record OptimizationImpact
{
    public string? DurationReduction { get; init; }
    public int? NetworkCallsReduction { get; init; }
    public string? ReliabilityImprovement { get; init; }
    public string? EstimatedNewDuration { get; init; }
}
