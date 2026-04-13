namespace SphereIntegrationHub.Definitions;

public sealed class WorkflowStageDefinition
{
    public string Name { get; set; } = string.Empty;
    public WorkflowStageKind Kind { get; set; }
    public string? ApiRef { get; set; }
    public string? Endpoint { get; set; }
    public string? HttpVerb { get; set; }
    public int? ExpectedStatus { get; set; }
    public int[]? ExpectedStatuses { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    public Dictionary<string, string>? Query { get; set; }
    public string? Body { get; set; }
    public string? BodyFile { get; set; }
    public string? DataFile { get; set; }
    public string? ForEach { get; set; }
    public bool? ForEachSequential { get; set; }
    public string? ItemName { get; set; }
    public string? IndexName { get; set; }
    public string? WorkflowRef { get; set; }
    public Dictionary<string, object?>? Inputs { get; set; }
    public Dictionary<string, string>? Debug { get; set; }
    public string? Message { get; set; }
    public Dictionary<string, string>? Output { get; set; }
    public List<string>? SecretOutputs { get; set; }
    public Dictionary<int, string>? JumpOnStatus { get; set; }
    public Dictionary<int, WorkflowStageStatusAction>? OnStatus { get; set; }
    public WorkflowStageEnsureDefinition? Ensure { get; set; }
    public int? DelaySeconds { get; set; }
    public string? AllowVersion { get; set; }
    public string? RunIf { get; set; }
    public Dictionary<string, string>? Set { get; set; }
    public Dictionary<string, string>? Context { get; set; }
    public WorkflowStageMockDefinition? Mock { get; set; }
    public WorkflowStageRetryDefinition? Retry { get; set; }
    public WorkflowStageCircuitBreakerDefinition? CircuitBreaker { get; set; }
    /// <summary>
    /// Defines outputs to register when this stage is skipped due to its runIf condition.
    /// Allows downstream stages to reference a canonical output key regardless of which
    /// branch in a mutually-exclusive set actually ran.
    /// </summary>
    public WorkflowStageOnSkipDefinition? OnSkip { get; set; }
}

public sealed class WorkflowStageOnSkipDefinition
{
    /// <summary>
    /// Output values to register for this stage when it is skipped.
    /// Templates are resolved against the execution context at the moment of skipping,
    /// so they can reference outputs from previously executed stages.
    /// </summary>
    public Dictionary<string, string>? Output { get; set; }
}

public sealed class WorkflowStageMockDefinition
{
    public int? Status { get; set; }
    public string? Payload { get; set; }
    public string? PayloadFile { get; set; }
    public Dictionary<string, string>? Output { get; set; }
}

public sealed class WorkflowStageStatusAction
{
    public string? JumpTo { get; set; }
    public Dictionary<string, string>? Output { get; set; }
    public string? Message { get; set; }
    public bool Fail { get; set; }
}

public sealed class WorkflowStageEnsureDefinition
{
    public string Mode { get; set; } = "CreateIfMissing";
    public int[]? ExistsOn { get; set; }
    public string? JumpTo { get; set; }
    public Dictionary<string, string>? Output { get; set; }
    public string? Message { get; set; }
}
