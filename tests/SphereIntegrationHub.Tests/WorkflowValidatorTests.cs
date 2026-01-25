using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;

namespace SphereIntegrationHub.Tests;

public sealed class WorkflowValidatorTests
{
    [Fact]
    public void Validate_FlagsDuplicateInitVariables()
    {
        var definition = new WorkflowDefinition
        {
            Version = "1.0",
            Id = "01",
            Name = "test",
            InitStage = new WorkflowInitStage
            {
                Variables = new List<WorkflowVariableDefinition>
                {
                    new() { Name = "dup", Type = RandomValueType.Text },
                    new() { Name = "dup", Type = RandomValueType.Text }
                }
            }
        };

        var document = new WorkflowDocument(definition, "/tmp/test.workflow", new Dictionary<string, string>());
        var validator = new WorkflowValidator(new WorkflowLoader());
        var errors = validator.Validate(document);

        Assert.Contains(errors, e => e.Contains("Duplicate init-stage variable name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsValueWithRange()
    {
        var definition = new WorkflowDefinition
        {
            Version = "1.0",
            Id = "01",
            Name = "test",
            InitStage = new WorkflowInitStage
            {
                Variables = new List<WorkflowVariableDefinition>
                {
                    new()
                    {
                        Name = "dateValue",
                        Type = RandomValueType.DateTime,
                        Value = "2025-01-01T00:00:00Z",
                        FromDateTime = DateTimeOffset.UtcNow.AddDays(-1)
                    }
                }
            }
        };

        var document = new WorkflowDocument(definition, "/tmp/test.workflow", new Dictionary<string, string>());
        var validator = new WorkflowValidator(new WorkflowLoader());
        var errors = validator.Validate(document);

        Assert.Contains(errors, e => e.Contains("cannot define value with range settings", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_AllowsStageJsonToken()
    {
        var definition = new WorkflowDefinition
        {
            Version = "1.0",
            Id = "01",
            Name = "test",
            References = new WorkflowReference
            {
                Apis = new List<ApiReferenceItem>
                {
                    new() { Name = "accounts", Definition = "accounts" }
                }
            },
            Stages = new List<WorkflowStageDefinition>
            {
                new()
                {
                    Name = "create",
                    Kind = WorkflowStageKind.Endpoint,
                    ApiRef = "accounts",
                    Endpoint = "/api/accounts",
                    HttpVerb = "POST",
                    ExpectedStatus = 200,
                    Output = new Dictionary<string, string>
                    {
                        ["dto"] = "{{response.body}}"
                    }
                }
            },
            EndStage = new WorkflowEndStage
            {
                Output = new Dictionary<string, string>
                {
                    ["id"] = "{{stage:json(create.output.dto).id}}"
                }
            }
        };

        var document = new WorkflowDocument(definition, "/tmp/test.workflow", new Dictionary<string, string>());
        var validator = new WorkflowValidator(new WorkflowLoader());
        var errors = validator.Validate(document);

        Assert.DoesNotContain(errors, e => e.Contains("stage:json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsStageDelayOverLimit()
    {
        var definition = new WorkflowDefinition
        {
            Version = "1.0",
            Id = "01",
            Name = "test",
            Stages = new List<WorkflowStageDefinition>
            {
                new()
                {
                    Name = "delayed",
                    Kind = WorkflowStageKind.Endpoint,
                    DelaySeconds = 61
                }
            }
        };

        var document = new WorkflowDocument(definition, "/tmp/test.workflow", new Dictionary<string, string>());
        var validator = new WorkflowValidator(new WorkflowLoader());
        var errors = validator.Validate(document);

        Assert.Contains(errors, e => e.Contains("delaySeconds", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsNegativeStageDelay()
    {
        var definition = new WorkflowDefinition
        {
            Version = "1.0",
            Id = "01",
            Name = "test",
            Stages = new List<WorkflowStageDefinition>
            {
                new()
                {
                    Name = "delayed",
                    Kind = WorkflowStageKind.Workflow,
                    DelaySeconds = -1
                }
            }
        };

        var document = new WorkflowDocument(definition, "/tmp/test.workflow", new Dictionary<string, string>());
        var validator = new WorkflowValidator(new WorkflowLoader());
        var errors = validator.Validate(document);

        Assert.Contains(errors, e => e.Contains("delaySeconds", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsResilienceOnWorkflowStage()
    {
        var definition = new WorkflowDefinition
        {
            Version = "1.0",
            Id = "01",
            Name = "test",
            Stages = new List<WorkflowStageDefinition>
            {
                new()
                {
                    Name = "child",
                    Kind = WorkflowStageKind.Workflow,
                    Retry = new WorkflowStageRetryDefinition
                    {
                        MaxRetries = 1,
                        DelayMs = 100,
                        HttpStatus = new[] { 500 }
                    },
                    CircuitBreaker = new WorkflowStageCircuitBreakerDefinition
                    {
                        FailureThreshold = 1,
                        BreakMs = 1000
                    }
                }
            }
        };

        var document = new WorkflowDocument(definition, "/tmp/test.workflow", new Dictionary<string, string>());
        var validator = new WorkflowValidator(new WorkflowLoader());
        var errors = validator.Validate(document);

        Assert.Contains(errors, e => e.Contains("retry is only supported for endpoint stages", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, e => e.Contains("circuitBreaker is only supported for endpoint stages", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsCircuitBreakerWithoutRetry()
    {
        var definition = new WorkflowDefinition
        {
            Version = "1.0",
            Id = "01",
            Name = "test",
            References = new WorkflowReference
            {
                Apis = new List<ApiReferenceItem>
                {
                    new() { Name = "accounts", Definition = "accounts" }
                }
            },
            Resilience = new WorkflowResilienceDefinition
            {
                CircuitBreakers = new Dictionary<string, CircuitBreakerDefinition>
                {
                    ["cb"] = new()
                    {
                        FailureThreshold = 1,
                        BreakMs = 1000
                    }
                }
            },
            Stages = new List<WorkflowStageDefinition>
            {
                new()
                {
                    Name = "create",
                    Kind = WorkflowStageKind.Endpoint,
                    ApiRef = "accounts",
                    Endpoint = "/api/accounts",
                    HttpVerb = "POST",
                    ExpectedStatus = 200,
                    CircuitBreaker = new WorkflowStageCircuitBreakerDefinition
                    {
                        Ref = "cb"
                    }
                }
            }
        };

        var document = new WorkflowDocument(definition, "/tmp/test.workflow", new Dictionary<string, string>());
        var validator = new WorkflowValidator(new WorkflowLoader());
        var errors = validator.Validate(document);

        Assert.Contains(errors, e => e.Contains("circuitBreaker requires retry", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsCircuitBreakerCloseOnSuccessAttempts()
    {
        var definition = new WorkflowDefinition
        {
            Version = "1.0",
            Id = "01",
            Name = "test",
            References = new WorkflowReference
            {
                Apis = new List<ApiReferenceItem>
                {
                    new() { Name = "accounts", Definition = "accounts" }
                }
            },
            Resilience = new WorkflowResilienceDefinition
            {
                CircuitBreakers = new Dictionary<string, CircuitBreakerDefinition>
                {
                    ["cb"] = new()
                    {
                        FailureThreshold = 1,
                        BreakMs = 1000,
                        CloseOnSuccessAttempts = 0
                    }
                }
            },
            Stages = new List<WorkflowStageDefinition>
            {
                new()
                {
                    Name = "create",
                    Kind = WorkflowStageKind.Endpoint,
                    ApiRef = "accounts",
                    Endpoint = "/api/accounts",
                    HttpVerb = "POST",
                    ExpectedStatus = 200,
                    Retry = new WorkflowStageRetryDefinition
                    {
                        MaxRetries = 1,
                        DelayMs = 1,
                        HttpStatus = [ 500 ]
                    },
                    CircuitBreaker = new WorkflowStageCircuitBreakerDefinition
                    {
                        Ref = "cb"
                    }
                }
            }
        };

        var document = new WorkflowDocument(definition, "/tmp/test.workflow", new Dictionary<string, string>());
        var validator = new WorkflowValidator(new WorkflowLoader());
        var errors = validator.Validate(document);

        Assert.Contains(errors, e => e.Contains("closeOnSuccessAttempts", StringComparison.OrdinalIgnoreCase));
    }
}
