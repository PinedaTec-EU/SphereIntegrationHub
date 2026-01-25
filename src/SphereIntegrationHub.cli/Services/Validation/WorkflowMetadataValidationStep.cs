using SphereIntegrationHub.Definitions;
using System;
using System.Collections.Generic;

namespace SphereIntegrationHub.Services;

internal sealed class WorkflowMetadataValidationStep : IWorkflowValidationStep
{
    public void Validate(WorkflowValidationContext context, List<string> errors)
    {
        var definition = context.Definition;

        if (string.IsNullOrWhiteSpace(definition.Version))
        {
            errors.Add("Workflow version is required.");
        }

        if (string.IsNullOrWhiteSpace(definition.Id))
        {
            errors.Add("Workflow id is required.");
        }

        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            errors.Add("Workflow name is required.");
        }

        ValidateInputs(definition.Input, errors);
        ValidateInitStage(definition.InitStage, errors);
        ValidateEndStage(definition, errors);
    }

    private static void ValidateInputs(IReadOnlyList<WorkflowInputDefinition>? inputs, List<string> errors)
    {
        if (inputs is null)
        {
            return;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in inputs)
        {
            if (string.IsNullOrWhiteSpace(input.Name))
            {
                errors.Add("Input name is required.");
                continue;
            }

            if (!names.Add(input.Name))
            {
                errors.Add($"Duplicate input name '{input.Name}'.");
            }
        }
    }

    private static void ValidateInitStage(WorkflowInitStage? initStage, List<string> errors)
    {
        if (initStage is null)
        {
            return;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var variable in initStage.Variables)
        {
            if (string.IsNullOrWhiteSpace(variable.Name))
            {
                errors.Add("Init-stage variable name is required.");
                continue;
            }

            if (!names.Add(variable.Name))
            {
                errors.Add($"Duplicate init-stage variable name '{variable.Name}'.");
            }

            if (!string.IsNullOrWhiteSpace(variable.Value) && HasVariableRangeConfig(variable))
            {
                errors.Add($"Init-stage variable '{variable.Name}' cannot define value with range settings.");
            }

            if (!string.IsNullOrWhiteSpace(variable.Value) &&
                (variable.Type == RandomValueType.DateTime ||
                 variable.Type == RandomValueType.Date ||
                 variable.Type == RandomValueType.Time))
            {
                errors.Add($"Init-stage variable '{variable.Name}' must use type 'Fixed' when value is provided.");
            }
        }
    }

    private static bool HasVariableRangeConfig(WorkflowVariableDefinition variable)
    {
        return variable.Min.HasValue ||
            variable.Max.HasValue ||
            variable.Padding.HasValue ||
            variable.Length.HasValue ||
            variable.FromDateTime.HasValue ||
            variable.ToDateTime.HasValue ||
            variable.FromDate.HasValue ||
            variable.ToDate.HasValue ||
            variable.FromTime.HasValue ||
            variable.ToTime.HasValue ||
            !string.IsNullOrWhiteSpace(variable.Format) ||
            variable.Start.HasValue ||
            variable.Step.HasValue;
    }

    private static void ValidateEndStage(WorkflowDefinition definition, List<string> errors)
    {
        if (!definition.Output)
        {
            return;
        }

        if (definition.EndStage?.Output is null || definition.EndStage.Output.Count == 0)
        {
            errors.Add("End-stage output is required when workflow output is enabled.");
        }
    }
}
