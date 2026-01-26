using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;

namespace SphereIntegrationHub.Services.Plugins;

internal sealed class HttpStagePlugin : IStagePlugin, IStageValidator
{
    public string Id => "http";

    public IReadOnlyCollection<string> StageKinds { get; } = new[]
    {
        WorkflowStageKinds.Endpoint,
        WorkflowStageKinds.Http
    };

    public StagePluginCapabilities Capabilities { get; } = new(
        StageOutputKind.Endpoint,
        StageMockKind.Endpoint,
        AllowsResponseTokens: true,
        SupportsJumpOnStatus: true,
        ContinueOnError: false);

    public void ValidateStage(WorkflowStageDefinition stage, StageValidationContext context, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(stage.ApiRef))
        {
            errors.Add($"Stage '{stage.Name}' apiRef is required for http stages.");
        }
        else if (!context.ApiReferences.Contains(stage.ApiRef))
        {
            errors.Add($"Stage '{stage.Name}' apiRef '{stage.ApiRef}' is not declared in references.apis.");
        }

        if (string.IsNullOrWhiteSpace(stage.Endpoint))
        {
            errors.Add($"Stage '{stage.Name}' endpoint is required for http stages.");
        }

        if (string.IsNullOrWhiteSpace(stage.HttpVerb))
        {
            errors.Add($"Stage '{stage.Name}' httpVerb is required for http stages.");
        }

        if (stage.ExpectedStatus is null || stage.ExpectedStatus <= 0)
        {
            errors.Add($"Stage '{stage.Name}' expectedStatus must be a positive integer.");
        }

        if (stage.JumpOnStatus is not null)
        {
            foreach (var jump in stage.JumpOnStatus)
            {
                if (jump.Key <= 0)
                {
                    errors.Add($"Stage '{stage.Name}' jump status must be a positive integer.");
                }
            }
        }

        if (stage.CircuitBreaker is not null && stage.Retry is null)
        {
            errors.Add($"Stage '{stage.Name}' circuitBreaker requires retry.");
        }

        ValidateStageRetry(context.Resilience, stage, errors);
        ValidateStageCircuitBreaker(context.Resilience, stage, errors);
    }

    public async Task<string?> ExecuteAsync(WorkflowStageDefinition stage, StageExecutionContext context, CancellationToken cancellationToken)
    {
        var executor = new EndpointStageExecutor(
            context.TemplateResolver,
            context.MockPayloadService,
            context.SystemTimeProvider,
            context.EndpointInvoker,
            context.Logger,
            context.StageMessageEmitter);

        var apiBaseUrls = ApiBaseUrlResolver.BuildApiBaseUrlLookup(
            context.Definition,
            context.CatalogVersion,
            context.Environment);

        return await executor.ExecuteAsync(
            context.Definition,
            stage,
            apiBaseUrls,
            context.Execution,
            context.Verbose,
            context.Document.FilePath,
            context.Mocked,
            cancellationToken);
    }

    private static void ValidateStageRetry(
        WorkflowResilienceDefinition? resilience,
        WorkflowStageDefinition stage,
        List<string> errors)
    {
        if (stage.Retry is null)
        {
            return;
        }

        if (stage.Retry.HttpStatus is null || stage.Retry.HttpStatus.Length == 0)
        {
            errors.Add($"Stage '{stage.Name}' retry httpStatus is required.");
        }
        else if (stage.Retry.HttpStatus.Any(status => status <= 0))
        {
            errors.Add($"Stage '{stage.Name}' retry httpStatus must contain positive integers.");
        }

        RetryPolicyDefinition? definition = null;
        if (!string.IsNullOrWhiteSpace(stage.Retry.Ref))
        {
            if (resilience?.Retries is null || !resilience.Retries.TryGetValue(stage.Retry.Ref, out definition))
            {
                errors.Add($"Stage '{stage.Name}' retry ref '{stage.Retry.Ref}' was not found in resilience.retries.");
            }
        }

        var maxRetries = stage.Retry.MaxRetries ?? definition?.MaxRetries;
        var delayMs = stage.Retry.DelayMs ?? definition?.DelayMs;

        if (maxRetries is null || maxRetries <= 0)
        {
            errors.Add($"Stage '{stage.Name}' retry maxRetries must be a positive integer.");
        }

        if (delayMs is null || delayMs <= 0)
        {
            errors.Add($"Stage '{stage.Name}' retry delayMs must be a positive integer.");
        }
    }

    private static void ValidateStageCircuitBreaker(
        WorkflowResilienceDefinition? resilience,
        WorkflowStageDefinition stage,
        List<string> errors)
    {
        if (stage.CircuitBreaker is null)
        {
            return;
        }

        CircuitBreakerDefinition? definition = null;
        if (!string.IsNullOrWhiteSpace(stage.CircuitBreaker.Ref))
        {
            if (resilience?.CircuitBreakers is null || !resilience.CircuitBreakers.TryGetValue(stage.CircuitBreaker.Ref, out definition))
            {
                errors.Add($"Stage '{stage.Name}' circuitBreaker ref '{stage.CircuitBreaker.Ref}' was not found in resilience.circuitBreakers.");
            }
        }

        var failureThreshold = stage.CircuitBreaker.FailureThreshold ?? definition?.FailureThreshold;
        var breakMs = stage.CircuitBreaker.BreakMs ?? definition?.BreakMs;
        var closeOnSuccessAttempts = stage.CircuitBreaker.CloseOnSuccessAttempts ?? definition?.CloseOnSuccessAttempts;

        if (failureThreshold is null || failureThreshold <= 0)
        {
            errors.Add($"Stage '{stage.Name}' circuitBreaker failureThreshold must be a positive integer.");
        }

        if (breakMs is null || breakMs <= 0)
        {
            errors.Add($"Stage '{stage.Name}' circuitBreaker breakMs must be a positive integer.");
        }

        if (closeOnSuccessAttempts is not null && closeOnSuccessAttempts <= 0)
        {
            errors.Add($"Stage '{stage.Name}' circuitBreaker closeOnSuccessAttempts must be a positive integer.");
        }
    }
}
