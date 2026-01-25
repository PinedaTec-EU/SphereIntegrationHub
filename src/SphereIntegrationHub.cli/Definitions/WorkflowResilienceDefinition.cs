namespace SphereIntegrationHub.Definitions;

public sealed class WorkflowResilienceDefinition
{
    public Dictionary<string, RetryPolicyDefinition>? Retries { get; set; }
    public Dictionary<string, CircuitBreakerDefinition>? CircuitBreakers { get; set; }
}

public sealed class RetryPolicyDefinition
{
    public int? MaxRetries { get; set; }
    public int? DelayMs { get; set; }
}

public sealed class CircuitBreakerDefinition
{
    public int? FailureThreshold { get; set; }
    public int? BreakMs { get; set; }
    public int? CloseOnSuccessAttempts { get; set; }
}

public sealed class WorkflowStageRetryDefinition
{
    public string? Ref { get; set; }
    public int? MaxRetries { get; set; }
    public int? DelayMs { get; set; }
    public int[]? HttpStatus { get; set; }
    public WorkflowStageRetryMessagesDefinition? Messages { get; set; }
}

public sealed class WorkflowStageCircuitBreakerDefinition
{
    public string? Ref { get; set; }
    public int? FailureThreshold { get; set; }
    public int? BreakMs { get; set; }
    public int? CloseOnSuccessAttempts { get; set; }
    public WorkflowStageCircuitBreakerMessagesDefinition? Messages { get; set; }
}

public sealed class WorkflowStageRetryMessagesDefinition
{
    public string? OnException { get; set; }
}

public sealed class WorkflowStageCircuitBreakerMessagesDefinition
{
    public string? OnOpen { get; set; }
    public string? OnBlocked { get; set; }
}
