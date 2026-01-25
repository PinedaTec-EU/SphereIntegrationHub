using SphereIntegrationHub.Definitions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SphereIntegrationHub.Services;

internal sealed class WorkflowStageValidationStep : IWorkflowValidationStep
{
    public void Validate(WorkflowValidationContext context, List<string> errors)
    {
        ValidateStages(
            context.Definition,
            context.WorkflowPath,
            context.EnvironmentVariables,
            context.Loader,
            errors);
    }

    private static void ValidateStages(
        WorkflowDefinition definition,
        string workflowPath,
        IReadOnlyDictionary<string, string> environmentVariables,
        WorkflowLoader loader,
        List<string> errors)
    {
        if (definition.Stages is null || definition.Stages.Count == 0)
        {
            return;
        }

        ValidateResilienceDefinitions(definition.Resilience, errors);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stageNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var references = definition.References;
        var workflowLookup = WorkflowReferenceLookupBuilder.BuildWorkflowReferenceLookup(references?.Workflows, workflowPath, errors);
        var apiLookup = WorkflowReferenceLookupBuilder.BuildApiReferenceLookup(references?.Apis, errors);

        foreach (var stage in definition.Stages)
        {
            if (string.IsNullOrWhiteSpace(stage.Name))
            {
                errors.Add("Stage name is required.");
                continue;
            }

            if (!names.Add(stage.Name))
            {
                errors.Add($"Duplicate stage name '{stage.Name}'.");
            }
            else
            {
                stageNames.Add(stage.Name);
            }

            if (stage.Kind == WorkflowStageKind.Endpoint)
            {
                if (stage.DelaySeconds is not null && (stage.DelaySeconds < 0 || stage.DelaySeconds > 60))
                {
                    errors.Add($"Stage '{stage.Name}' delaySeconds must be between 0 and 60.");
                }

                if (string.IsNullOrWhiteSpace(stage.ApiRef))
                {
                    errors.Add($"Stage '{stage.Name}' apiRef is required for endpoint stages.");
                }
                else if (!apiLookup.Contains(stage.ApiRef))
                {
                    errors.Add($"Stage '{stage.Name}' apiRef '{stage.ApiRef}' is not declared in references.apis.");
                }

                if (string.IsNullOrWhiteSpace(stage.Endpoint))
                {
                    errors.Add($"Stage '{stage.Name}' endpoint is required for endpoint stages.");
                }

                if (string.IsNullOrWhiteSpace(stage.HttpVerb))
                {
                    errors.Add($"Stage '{stage.Name}' httpVerb is required for endpoint stages.");
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

                ValidateStageRetry(definition.Resilience, stage, errors);
                ValidateStageCircuitBreaker(definition.Resilience, stage, errors);
            }
            else if (stage.Kind == WorkflowStageKind.Workflow)
            {
                if (stage.DelaySeconds is not null && (stage.DelaySeconds < 0 || stage.DelaySeconds > 60))
                {
                    errors.Add($"Stage '{stage.Name}' delaySeconds must be between 0 and 60.");
                }

                if (stage.Retry is not null)
                {
                    errors.Add($"Stage '{stage.Name}' retry is only supported for endpoint stages.");
                }

                if (stage.CircuitBreaker is not null)
                {
                    errors.Add($"Stage '{stage.Name}' circuitBreaker is only supported for endpoint stages.");
                }

                if (string.IsNullOrWhiteSpace(stage.WorkflowRef))
                {
                    errors.Add($"Stage '{stage.Name}' workflowRef is required for workflow stages.");
                    continue;
                }

                if (!workflowLookup.TryGetValue(stage.WorkflowRef, out var referencePath))
                {
                    errors.Add($"Stage '{stage.Name}' workflowRef '{stage.WorkflowRef}' is not declared in references.");
                    continue;
                }

                if (!File.Exists(referencePath))
                {
                    errors.Add($"Referenced workflow '{stage.WorkflowRef}' was not found at '{referencePath}'.");
                    continue;
                }

                try
                {
                    var referencedWorkflow = loader.Load(referencePath, environmentVariables);
                    ValidateWorkflowStageInputs(stage, referencedWorkflow.Definition, errors);
                    ValidateWorkflowVersion(stage, definition.Version, referencedWorkflow.Definition.Version, errors);
                }
                catch (Exception ex)
                {
                    errors.Add($"Referenced workflow '{stage.WorkflowRef}' failed to load: {ex.Message}");
                }
            }
        }

        ValidateJumps(definition.Stages, stageNames, errors);
    }

    private static void ValidateJumps(
        IReadOnlyList<WorkflowStageDefinition> stages,
        HashSet<string> stageNames,
        List<string> errors)
    {
        foreach (var stage in stages)
        {
            if (stage.JumpOnStatus is null || stage.JumpOnStatus.Count == 0)
            {
                continue;
            }

            if (stage.Kind != WorkflowStageKind.Endpoint)
            {
                errors.Add($"Stage '{stage.Name}' jumpOnStatus is only supported for endpoint stages.");
                continue;
            }

            foreach (var target in stage.JumpOnStatus.Values)
            {
                if (string.IsNullOrWhiteSpace(target))
                {
                    errors.Add($"Stage '{stage.Name}' has an empty jump target.");
                    continue;
                }

                if (!string.Equals(target, "endStage", StringComparison.OrdinalIgnoreCase) &&
                    !stageNames.Contains(target))
                {
                    errors.Add($"Stage '{stage.Name}' jump target '{target}' does not exist.");
                }
            }
        }
    }

    private static void ValidateWorkflowStageInputs(
        WorkflowStageDefinition stage,
        WorkflowDefinition referencedWorkflow,
        List<string> errors)
    {
        var definedInputs = referencedWorkflow.Input is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(referencedWorkflow.Input.Select(input => input.Name), StringComparer.OrdinalIgnoreCase);

        var providedInputs = stage.Inputs is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(stage.Inputs.Keys, StringComparer.OrdinalIgnoreCase);

        if (referencedWorkflow.Input is not null)
        {
            foreach (var input in referencedWorkflow.Input)
            {
                if (input.Required && !providedInputs.Contains(input.Name))
                {
                    errors.Add($"Stage '{stage.Name}' is missing required input '{input.Name}' for workflow '{referencedWorkflow.Name}'.");
                }
            }
        }

        foreach (var provided in providedInputs)
        {
            if (!definedInputs.Contains(provided))
            {
                errors.Add($"Stage '{stage.Name}' provides unknown input '{provided}' for workflow '{referencedWorkflow.Name}'.");
            }
        }
    }

    private static void ValidateWorkflowVersion(
        WorkflowStageDefinition stage,
        string parentVersion,
        string referencedVersion,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(referencedVersion) || string.IsNullOrWhiteSpace(parentVersion))
        {
            return;
        }

        if (string.Equals(parentVersion, referencedVersion, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(stage.AllowVersion) &&
            string.Equals(stage.AllowVersion, referencedVersion, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        errors.Add($"Stage '{stage.Name}' references workflow version '{referencedVersion}' which differs from parent version '{parentVersion}'.");
    }

    private static void ValidateResilienceDefinitions(WorkflowResilienceDefinition? resilience, List<string> errors)
    {
        if (resilience?.Retries is not null)
        {
            foreach (var pair in resilience.Retries)
            {
                if (pair.Value is null)
                {
                    errors.Add($"Retry policy '{pair.Key}' must define maxRetries and delayMs.");
                    continue;
                }

                if (pair.Value.MaxRetries is null || pair.Value.MaxRetries <= 0)
                {
                    errors.Add($"Retry policy '{pair.Key}' maxRetries must be a positive integer.");
                }

                if (pair.Value.DelayMs is null || pair.Value.DelayMs <= 0)
                {
                    errors.Add($"Retry policy '{pair.Key}' delayMs must be a positive integer.");
                }
            }
        }

        if (resilience?.CircuitBreakers is not null)
        {
            foreach (var pair in resilience.CircuitBreakers)
            {
                if (pair.Value is null)
                {
                    errors.Add($"Circuit breaker '{pair.Key}' must define failureThreshold and breakMs.");
                    continue;
                }

                if (pair.Value.FailureThreshold is null || pair.Value.FailureThreshold <= 0)
                {
                    errors.Add($"Circuit breaker '{pair.Key}' failureThreshold must be a positive integer.");
                }

                if (pair.Value.BreakMs is null || pair.Value.BreakMs <= 0)
                {
                    errors.Add($"Circuit breaker '{pair.Key}' breakMs must be a positive integer.");
                }
            }
        }
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
