namespace SphereIntegrationHub.MCP.Models;

/// <summary>
/// Represents how data flows between multiple endpoints
/// </summary>
public sealed record DataFlowGraph
{
    public required string Version { get; init; }
    public List<DataFlowStage> Stages { get; init; } = [];
    public List<DataFlowConnection> Connections { get; init; } = [];
    public List<string> ExecutionOrder { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}

public sealed record DataFlowStage
{
    public required string StageId { get; init; }
    public required string ApiName { get; init; }
    public required string Endpoint { get; init; }
    public required string HttpVerb { get; init; }
    public List<DataField> Inputs { get; init; } = [];
    public List<DataField> Outputs { get; init; } = [];
}

public sealed record DataField
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required string Location { get; init; } // "body", "header", "query", "path", "response"
    public bool Required { get; init; }
}

public sealed record DataFlowConnection
{
    public required string FromStage { get; init; }
    public required string FromField { get; init; }
    public required string ToStage { get; init; }
    public required string ToField { get; init; }
    public required string ToLocation { get; init; }
    public required double Confidence { get; init; }
    public required string Binding { get; init; } // The actual {{stage:x.output.y}} binding
}
