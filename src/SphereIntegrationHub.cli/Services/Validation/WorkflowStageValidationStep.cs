using System;
using System.Collections.Generic;
using System.Linq;

using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services.Plugins;

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
            context.MockPayloadService,
            context.StageValidators,
            context.StagePlugins,
            errors);
    }

    private static void ValidateStages(
        WorkflowDefinition definition,
        string workflowPath,
        IReadOnlyDictionary<string, string> environmentVariables,
        WorkflowLoader loader,
        MockPayloadService mockPayloadService,
        StageValidatorRegistry stageValidators,
        StagePluginRegistry stagePlugins,
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
        var stageContext = new StageValidationContext(
            new WorkflowDocument(definition, workflowPath, environmentVariables),
            loader,
            mockPayloadService,
            workflowLookup,
            apiLookup);

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

            if (stage.DelaySeconds is not null && (stage.DelaySeconds < 0 || stage.DelaySeconds > 60))
            {
                errors.Add($"Stage '{stage.Name}' delaySeconds must be between 0 and 60.");
            }

            if (string.IsNullOrWhiteSpace(stage.Kind))
            {
                errors.Add($"Stage '{stage.Name}' kind is required.");
                continue;
            }

            if (!stageValidators.TryGetByKind(stage.Kind, out var validator))
            {
                errors.Add($"Stage '{stage.Name}' kind '{stage.Kind}' does not match any loaded validator.");
                continue;
            }

            validator.ValidateStage(stage, stageContext, errors);
        }

        ValidateJumps(definition.Stages, stageNames, stagePlugins, errors);
    }

    private static void ValidateJumps(
        IReadOnlyList<WorkflowStageDefinition> stages,
        HashSet<string> stageNames,
        StagePluginRegistry stagePlugins,
        List<string> errors)
    {
        foreach (var stage in stages)
        {
            if (stage.JumpOnStatus is null || stage.JumpOnStatus.Count == 0)
            {
                continue;
            }

            if (!stagePlugins.TryGetByKind(stage.Kind, out var plugin))
            {
                errors.Add($"Stage '{stage.Name}' jumpOnStatus requires a loaded plugin.");
                continue;
            }

            if (!plugin.Capabilities.SupportsJumpOnStatus)
            {
                errors.Add($"Stage '{stage.Name}' jumpOnStatus is not supported for kind '{stage.Kind}'.");
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

}
