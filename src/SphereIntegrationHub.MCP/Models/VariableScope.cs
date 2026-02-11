namespace SphereIntegrationHub.MCP.Models;

/// <summary>
/// Available variables at a specific point in workflow execution
/// </summary>
public sealed record VariableScope
{
    public List<InputVariable> Inputs { get; init; } = [];
    public List<GlobalVariable> Globals { get; init; } = [];
    public List<ContextVariable> Context { get; init; } = [];
    public List<EnvironmentVariable> Env { get; init; } = [];
    public List<SystemVariable> System { get; init; } = [];
    public List<StageOutputVariable> StageOutputs { get; init; } = [];
}

public sealed record InputVariable
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required bool Required { get; init; }
}

public sealed record GlobalVariable
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required string Value { get; init; }
}

public sealed record ContextVariable
{
    public required string Name { get; init; }
    public required string Source { get; init; }
}

public sealed record EnvironmentVariable
{
    public required string Name { get; init; }
    public required string Value { get; init; }
}

public sealed record SystemVariable
{
    public required string Token { get; init; }
    public required string Description { get; init; }
}

public sealed record StageOutputVariable
{
    public required string Stage { get; init; }
    public List<OutputField> Outputs { get; init; } = [];
}

public sealed record OutputField
{
    public required string Name { get; init; }
    public required string Type { get; init; }
}
