namespace SphereIntegrationHub.Definitions;

public sealed class WorkflowStageDefinition
{
    public string Name { get; set; } = string.Empty;
    public WorkflowStageKind Kind { get; set; }
    public string? ApiRef { get; set; }
    public string? Endpoint { get; set; }
    public string? HttpVerb { get; set; }
    public int? ExpectedStatus { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    public Dictionary<string, string>? Query { get; set; }
    public string? Body { get; set; }
    public string? WorkflowRef { get; set; }
    public Dictionary<string, string>? Inputs { get; set; }
    public Dictionary<string, string>? Debug { get; set; }
    public string? Message { get; set; }
    public Dictionary<string, string>? Output { get; set; }
    public Dictionary<int, string>? JumpOnStatus { get; set; }
    public int? DelaySeconds { get; set; }
    public string? AllowVersion { get; set; }
    public string? RunIf { get; set; }
    public Dictionary<string, string>? Set { get; set; }
    public Dictionary<string, string>? Context { get; set; }
    public WorkflowStageMockDefinition? Mock { get; set; }
    public WorkflowStageRetryDefinition? Retry { get; set; }
    public WorkflowStageCircuitBreakerDefinition? CircuitBreaker { get; set; }
}

public sealed class WorkflowStageMockDefinition
{
    public int? Status { get; set; }
    public string? Payload { get; set; }
    public string? PayloadFile { get; set; }
    public Dictionary<string, string>? Output { get; set; }
}
