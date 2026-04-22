using System.Text.Json;
using System.Globalization;

using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Plugins;

namespace SphereIntegrationHub.Services;

public sealed class WorkflowValidator
{
    private static readonly IReadOnlyDictionary<string, string> EmptyEnvironmentVariables =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private readonly WorkflowLoader _loader;
    private readonly MockPayloadService _mockPayloadService;
    private readonly StagePluginRegistry _stagePluginRegistry;

    public WorkflowValidator(WorkflowLoader loader, StagePluginRegistry? stagePluginRegistry = null)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _mockPayloadService = new MockPayloadService();
        _stagePluginRegistry = stagePluginRegistry ?? new StagePluginRegistryBuilder().CreateBuiltInRegistry();
    }

    public IReadOnlyList<string> Validate(
        WorkflowDocument document,
        IReadOnlyDictionary<string, string>? runtimeInputs = null)
        => ValidateWithDetails(document, runtimeInputs).Errors;

    public WorkflowValidationResult ValidateWithDetails(
        WorkflowDocument document,
        IReadOnlyDictionary<string, string>? runtimeInputs = null)
    {
        using var activity = Telemetry.ActivitySource.StartActivity(TelemetryConstants.ActivityWorkflowValidate);
        activity?.SetTag(TelemetryConstants.TagWorkflowName, document.Definition.Name);
        var errors = new List<string>();
        var warnings = new List<string>();
        var definition = document.Definition;

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
        ValidateStages(definition, document.FilePath, document.EnvironmentVariables, runtimeInputs, errors, warnings);
        ValidateEndStage(definition, errors);
        ValidateVariableReferences(definition, document.FilePath, document.EnvironmentVariables, runtimeInputs, errors, warnings);

        return new WorkflowValidationResult(errors, warnings);
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
            CheckUniqueName(input.Name, names, "Input", errors);
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
            if (!CheckUniqueName(variable.Name, names, "Init-stage variable", errors))
            {
                continue;
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

    private static bool CheckUniqueName(string? name, HashSet<string> seen, string label, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add($"{label} name is required.");
            return false;
        }

        if (!seen.Add(name))
        {
            errors.Add($"Duplicate {label} name '{name}'.");
            return false;
        }

        return true;
    }

    private static bool HasVariableRangeConfig(WorkflowVariableDefinition variable)
    {
        return variable.Min.HasValue ||
            variable.Max.HasValue ||
            variable.Padding.HasValue ||
            variable.Length.HasValue ||
            !string.IsNullOrWhiteSpace(variable.CharacterSet) ||
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

    private void ValidateStages(
        WorkflowDefinition definition,
        string workflowPath,
        IReadOnlyDictionary<string, string> environmentVariables,
        IReadOnlyDictionary<string, string>? runtimeInputs,
        List<string> errors,
        List<string> warnings)
    {
        if (definition.Stages is null || definition.Stages.Count == 0)
        {
            return;
        }

        ValidateResilienceDefinitions(definition.Resilience, errors);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stageNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var references = definition.References;
        var validationGlobals = BuildValidationGlobals(definition.InitStage, environmentVariables, runtimeInputs);
        var workflowLookup = BuildWorkflowLookupForValidation(
            references?.Workflows,
            workflowPath,
            environmentVariables,
            runtimeInputs,
            validationGlobals,
            errors,
            warnings);
        var apiLookup = BuildApiReferenceLookup(references?.Apis, errors);

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

            if (!WorkflowStageKind.IsWorkflow(stage.Kind))
            {
                if (stage.DelaySeconds is not null && (stage.DelaySeconds < 0 || stage.DelaySeconds > 60))
                {
                    errors.Add($"Stage '{stage.Name}' delaySeconds must be between 0 and 60.");
                }

                if (!string.IsNullOrWhiteSpace(stage.ApiRef) && !apiLookup.Contains(stage.ApiRef))
                {
                    errors.Add($"Stage '{stage.Name}' apiRef '{stage.ApiRef}' is not declared in references.apis.");
                }

                if ((stage.ExpectedStatus is null || stage.ExpectedStatus <= 0) &&
                    (stage.ExpectedStatuses is null || stage.ExpectedStatuses.Length == 0))
                {
                    errors.Add($"Stage '{stage.Name}' expectedStatus or expectedStatuses is required.");
                }

                if (stage.ExpectedStatus is not null && stage.ExpectedStatus <= 0)
                {
                    errors.Add($"Stage '{stage.Name}' expectedStatus must be a positive integer.");
                }

                if (stage.ExpectedStatuses is not null && stage.ExpectedStatuses.Any(status => status <= 0))
                {
                    errors.Add($"Stage '{stage.Name}' expectedStatuses must contain positive integers.");
                }

                if (!string.IsNullOrWhiteSpace(stage.Body) && !string.IsNullOrWhiteSpace(stage.BodyFile))
                {
                    errors.Add($"Stage '{stage.Name}' cannot define both body and bodyFile.");
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

                if (stage.OnStatus is not null)
                {
                    foreach (var item in stage.OnStatus)
                    {
                        if (item.Key <= 0)
                        {
                            errors.Add($"Stage '{stage.Name}' onStatus code must be a positive integer.");
                        }
                    }
                }

                if (stage.Ensure is not null)
                {
                    if (!string.Equals(stage.Ensure.Mode, WorkflowConstants.EnsureModeCreateIfMissing, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(stage.Ensure.Mode, WorkflowConstants.EnsureModeUpsert, StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add($"Stage '{stage.Name}' ensure.mode must be {WorkflowConstants.EnsureModeCreateIfMissing} or {WorkflowConstants.EnsureModeUpsert}.");
                    }

                    if (stage.Ensure.ExistsOn is not null && stage.Ensure.ExistsOn.Any(status => status <= 0))
                    {
                        errors.Add($"Stage '{stage.Name}' ensure.existsOn must contain positive integers.");
                    }
                }

                if (stage.CircuitBreaker is not null && stage.Retry is null)
                {
                    errors.Add($"Stage '{stage.Name}' circuitBreaker requires retry.");
                }

                ValidateStageRetry(definition.Resilience, stage, errors);
                ValidateStageCircuitBreaker(definition.Resilience, stage, errors);

                if (!_stagePluginRegistry.TryGetByKind(stage.Kind, out var plugin))
                {
                    errors.Add($"Stage '{stage.Name}' kind '{stage.Kind}' is not registered by any active plugin.");
                    continue;
                }

                plugin.ValidateStage(
                    stage,
                    new StagePluginValidationContext(definition, workflowPath, null, null, ValidateRequiredParameters: false),
                    errors,
                    warnings);
            }
            else
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

                if (stage.Ensure is not null)
                {
                    errors.Add($"Stage '{stage.Name}' ensure is only supported for endpoint stages.");
                }

                if (string.IsNullOrWhiteSpace(stage.WorkflowRef))
                {
                    errors.Add($"Stage '{stage.Name}' workflowRef is required for workflow stages.");
                    continue;
                }

                var declaredReference = references?.Workflows?.FirstOrDefault(reference =>
                    string.Equals(reference.Name, stage.WorkflowRef, StringComparison.OrdinalIgnoreCase));

                if (declaredReference is null)
                {
                    errors.Add($"Stage '{stage.Name}' workflowRef '{stage.WorkflowRef}' is not declared in references.");
                    continue;
                }

                if (!workflowLookup.TryGetValue(stage.WorkflowRef, out var referencePath))
                {
                    warnings.Add($"Stage '{stage.Name}' workflowRef '{stage.WorkflowRef}' will be validated fully at runtime because its path depends on deferred values.");
                    continue;
                }

                if (!File.Exists(referencePath))
                {
                    errors.Add($"Referenced workflow '{stage.WorkflowRef}' was not found at '{referencePath}'.");
                    continue;
                }

                try
                {
                    var referencedWorkflow = _loader.Load(referencePath, environmentVariables);
                    ValidateWorkflowStageInputs(stage, referencedWorkflow.Definition, errors);
                    ValidateWorkflowVersion(stage, definition.Version, referencedWorkflow.Definition.Version, warnings);
                }
                catch (Exception ex)
                {
                    errors.Add($"Referenced workflow '{stage.WorkflowRef}' failed to load: {ex.Message}");
                }
            }
        }

        ValidateJumps(definition.Stages, stageNames, errors);
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

    private static void ValidateJumps(
        IReadOnlyList<WorkflowStageDefinition> stages,
        HashSet<string> stageNames,
        List<string> errors)
    {
        foreach (var stage in stages)
        {
            if ((stage.JumpOnStatus is null || stage.JumpOnStatus.Count == 0) &&
                (stage.OnStatus is null || stage.OnStatus.Count == 0))
            {
                continue;
            }

            if (WorkflowStageKind.IsWorkflow(stage.Kind))
            {
                errors.Add($"Stage '{stage.Name}' jumpOnStatus is only supported for endpoint stages.");
                continue;
            }

            IEnumerable<string> jumpTargets = stage.JumpOnStatus is null
                ? Array.Empty<string>()
                : stage.JumpOnStatus.Values;
            foreach (var target in jumpTargets)
            {
                if (string.IsNullOrWhiteSpace(target))
                {
                    errors.Add($"Stage '{stage.Name}' has an empty jump target.");
                    continue;
                }

                if (!string.Equals(target, WorkflowConstants.EndStage, StringComparison.OrdinalIgnoreCase) &&
                    !stageNames.Contains(target))
                {
                    errors.Add($"Stage '{stage.Name}' jump target '{target}' does not exist.");
                }
            }

            if (stage.OnStatus is not null)
            {
                foreach (var action in stage.OnStatus.Values)
                {
                    if (string.IsNullOrWhiteSpace(action.JumpTo))
                    {
                        continue;
                    }

                    if (!string.Equals(action.JumpTo, WorkflowConstants.EndStage, StringComparison.OrdinalIgnoreCase) &&
                        !stageNames.Contains(action.JumpTo))
                    {
                        errors.Add($"Stage '{stage.Name}' onStatus jump target '{action.JumpTo}' does not exist.");
                    }
                }
            }

            if (stage.Ensure is not null &&
                !string.IsNullOrWhiteSpace(stage.Ensure.JumpTo) &&
                !string.Equals(stage.Ensure.JumpTo, WorkflowConstants.EndStage, StringComparison.OrdinalIgnoreCase) &&
                !stageNames.Contains(stage.Ensure.JumpTo))
            {
                errors.Add($"Stage '{stage.Name}' ensure jump target '{stage.Ensure.JumpTo}' does not exist.");
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
        List<string> warnings)
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

        warnings.Add($"Stage '{stage.Name}' references workflow version '{referencedVersion}' which differs from parent version '{parentVersion}'.");
    }

    private void ValidateVariableReferences(
        WorkflowDefinition definition,
        string workflowPath,
        IReadOnlyDictionary<string, string> environmentVariables,
        IReadOnlyDictionary<string, string>? runtimeInputs,
        List<string> errors,
        List<string> warnings)
    {
        var inputNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (definition.Input is not null)
        {
            foreach (var input in definition.Input)
            {
                if (!string.IsNullOrWhiteSpace(input.Name))
                {
                    inputNames.Add(input.Name);
                }
            }
        }

        var globalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (definition.InitStage?.Variables is not null)
        {
            foreach (var variable in definition.InitStage.Variables)
            {
                if (!string.IsNullOrWhiteSpace(variable.Name))
                {
                    globalNames.Add(variable.Name);
                    if (inputNames.Contains(variable.Name))
                    {
                        errors.Add($"Init-stage variable '{variable.Name}' duplicates an input with the same name.");
                    }
                }
            }
        }

        var endpointOutputs = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var workflowOutputs = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var validationGlobals = BuildValidationGlobals(definition.InitStage, environmentVariables, runtimeInputs);

        var workflowRefs = BuildWorkflowLookupForValidation(
            definition.References?.Workflows,
            workflowPath,
            environmentVariables,
            runtimeInputs,
            validationGlobals,
            errors,
            warnings);

        if (definition.Stages is not null)
        {
            foreach (var stage in definition.Stages)
            {
                if (!WorkflowStageKind.IsWorkflow(stage.Kind))
                {
                    var outputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "http_status"
                    };
                    if (stage.Output is not null)
                    {
                        foreach (var key in stage.Output.Keys)
                        {
                            outputs.Add(key);
                        }
                    }

                    endpointOutputs[stage.Name] = outputs;
                }
                else if (WorkflowStageKind.IsWorkflow(stage.Kind) && !string.IsNullOrWhiteSpace(stage.WorkflowRef))
                {
                    if (workflowRefs.TryGetValue(stage.WorkflowRef, out var referencePath))
                    {
                        try
                        {
                            var nested = _loader.Load(referencePath, environmentVariables);
                            var nestedOutputs = nested.Definition.EndStage?.Output?.Keys;
                            workflowOutputs[stage.Name] = nestedOutputs is null
                                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                                : new HashSet<string>(nestedOutputs, StringComparer.OrdinalIgnoreCase);
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"Failed to inspect workflow '{stage.WorkflowRef}': {ex.Message}");
                        }
                    }
                }
            }
        }

        if (definition.InitStage?.Variables is not null)
        {
            foreach (var variable in definition.InitStage.Variables)
            {
                ValidateTemplate(variable.Value, inputNames, globalNames, environmentVariables, endpointOutputs, workflowOutputs, "init-stage variable", errors);
            }
        }

        if (definition.InitStage?.Context is not null)
        {
            foreach (var value in definition.InitStage.Context.Values)
            {
                ValidateTemplate(value, inputNames, globalNames, environmentVariables, endpointOutputs, workflowOutputs, "init-stage context", errors);
            }
        }

        if (definition.Stages is not null)
        {
            foreach (var stage in definition.Stages)
            {
                var responseSample = TryLoadEndpointMockResponseSample(stage, workflowPath, environmentVariables, runtimeInputs);

                if (stage.Headers is not null)
                {
                    foreach (var header in stage.Headers.Values)
                    {
                        ValidateTemplate(header, inputNames, globalNames, environmentVariables, endpointOutputs, workflowOutputs, $"stage '{stage.Name}' header", errors);
                    }
                }

                if (stage.Query is not null)
                {
                    foreach (var query in stage.Query.Values)
                    {
                        ValidateTemplate(query, inputNames, globalNames, environmentVariables, endpointOutputs, workflowOutputs, $"stage '{stage.Name}' query", errors);
                    }
                }

                if (!string.IsNullOrWhiteSpace(stage.Body))
                {
                    ValidateTemplate(stage.Body, inputNames, globalNames, environmentVariables, endpointOutputs, workflowOutputs, $"stage '{stage.Name}' body", errors);
                }

                if (!string.IsNullOrWhiteSpace(stage.BodyFile))
                {
                    if (TryResolvePathForValidation(stage.BodyFile, workflowPath, environmentVariables, runtimeInputs, globals: null, out var resolvedPath, out var resolutionError, out var resolutionWarning))
                    {
                        if (!File.Exists(resolvedPath))
                        {
                            errors.Add($"Stage '{stage.Name}' bodyFile '{stage.BodyFile}' was not found.");
                        }
                        else
                        {
                            ValidateTemplate(File.ReadAllText(resolvedPath), inputNames, globalNames, environmentVariables, endpointOutputs, workflowOutputs, $"stage '{stage.Name}' bodyFile", errors);
                        }
                    }
                    else if (resolutionWarning is not null)
                    {
                        warnings.Add($"Stage '{stage.Name}' bodyFile '{stage.BodyFile}' will be resolved at runtime: {resolutionWarning}");
                    }
                    else if (resolutionError is not null)
                    {
                        errors.Add($"Stage '{stage.Name}' bodyFile '{stage.BodyFile}' could not be resolved: {resolutionError}");
                    }
                }

                if (!string.IsNullOrWhiteSpace(stage.DataFile))
                {
                    if (TryResolvePathForValidation(stage.DataFile, workflowPath, environmentVariables, runtimeInputs, globals: null, out var resolvedPath, out var resolutionError, out var resolutionWarning))
                    {
                        if (!File.Exists(resolvedPath))
                        {
                            errors.Add($"Stage '{stage.Name}' dataFile '{stage.DataFile}' was not found.");
                        }
                    }
                    else if (resolutionWarning is not null)
                    {
                        warnings.Add($"Stage '{stage.Name}' dataFile '{stage.DataFile}' will be resolved at runtime: {resolutionWarning}");
                    }
                    else if (resolutionError is not null)
                    {
                        errors.Add($"Stage '{stage.Name}' dataFile '{stage.DataFile}' could not be resolved: {resolutionError}");
                    }
                }

                if (stage.Inputs is not null)
                {
                    foreach (var input in stage.Inputs.Values)
                    {
                        WorkflowStageInputValueHelper.ValidateTemplates(
                            input,
                            template => ValidateTemplate(template, inputNames, globalNames, environmentVariables, endpointOutputs, workflowOutputs, $"stage '{stage.Name}' input", errors));
                    }
                }

                if (stage.Debug is not null)
                {
                    foreach (var value in stage.Debug.Values)
                    {
                        ValidateTemplate(value, inputNames, globalNames, environmentVariables, endpointOutputs, workflowOutputs, $"stage '{stage.Name}' debug", errors);
                    }
                }

                if (!string.IsNullOrWhiteSpace(stage.Message))
                {
                    ValidateTemplate(stage.Message, inputNames, globalNames, environmentVariables, endpointOutputs, workflowOutputs, $"stage '{stage.Name}' message", errors, allowResponse: !WorkflowStageKind.IsWorkflow(stage.Kind), responseSample: responseSample);
                }

                if (stage.Output is not null)
                {
                    foreach (var output in stage.Output.Values)
                    {
                        ValidateTemplate(output, inputNames, globalNames, environmentVariables, endpointOutputs, workflowOutputs, $"stage '{stage.Name}' output", errors, allowResponse: !WorkflowStageKind.IsWorkflow(stage.Kind), responseSample: responseSample);
                    }
                }

                if (stage.Set is not null)
                {
                    foreach (var value in stage.Set.Values)
                    {
                        ValidateTemplate(value, inputNames, globalNames, environmentVariables, endpointOutputs, workflowOutputs, $"stage '{stage.Name}' set", errors);
                    }
                }

                if (stage.Context is not null)
                {
                    foreach (var value in stage.Context.Values)
                    {
                        ValidateTemplate(value, inputNames, globalNames, environmentVariables, endpointOutputs, workflowOutputs, $"stage '{stage.Name}' context", errors);
                    }
                }

                if (!string.IsNullOrWhiteSpace(stage.RunIf))
                {
                    ValidateRunIf(stage.RunIf, inputNames, globalNames, environmentVariables, endpointOutputs, workflowOutputs, $"stage '{stage.Name}' runIf", errors);
                }

                if (!string.IsNullOrWhiteSpace(stage.ForEach))
                {
                    ValidateRunIf(stage.ForEach, inputNames, globalNames, environmentVariables, endpointOutputs, workflowOutputs, $"stage '{stage.Name}' forEach", errors, requireExpressionSyntax: false);
                }

                if (stage.OnStatus is not null)
                {
                    foreach (var action in stage.OnStatus.Values)
                    {
                        if (action.Output is not null)
                        {
                            foreach (var output in action.Output.Values)
                            {
                                ValidateTemplate(output, inputNames, globalNames, environmentVariables, endpointOutputs, workflowOutputs, $"stage '{stage.Name}' onStatus output", errors, allowResponse: true, responseSample: responseSample);
                            }
                        }
                    }
                }

                if (stage.Ensure?.Output is not null)
                {
                    foreach (var output in stage.Ensure.Output.Values)
                    {
                        ValidateTemplate(output, inputNames, globalNames, environmentVariables, endpointOutputs, workflowOutputs, $"stage '{stage.Name}' ensure output", errors, allowResponse: true, responseSample: responseSample);
                    }
                }

                if (stage.Mock is not null)
                {
                    ValidateMockDefinition(stage, workflowPath, inputNames, globalNames, environmentVariables, runtimeInputs, endpointOutputs, workflowOutputs, errors, warnings);
                }
            }
        }

        if (definition.EndStage?.Output is not null)
        {
            foreach (var output in definition.EndStage.Output.Values)
            {
                ValidateTemplate(output, inputNames, globalNames, environmentVariables, endpointOutputs, workflowOutputs, "end-stage output", errors);
            }
        }

        if (definition.EndStage?.Context is not null)
        {
            foreach (var value in definition.EndStage.Context.Values)
            {
                ValidateTemplate(value, inputNames, globalNames, environmentVariables, endpointOutputs, workflowOutputs, "end-stage context", errors);
            }
        }

        if (!string.IsNullOrWhiteSpace(definition.EndStage?.Result?.Message))
        {
            ValidateTemplate(definition.EndStage.Result.Message, inputNames, globalNames, environmentVariables, endpointOutputs, workflowOutputs, "end-stage result message", errors);
        }
    }

    private static void ValidateTemplate(
        string? template,
        HashSet<string> inputs,
        HashSet<string> globals,
        IReadOnlyDictionary<string, string> environmentVariables,
        Dictionary<string, HashSet<string>> endpointOutputs,
        Dictionary<string, HashSet<string>> workflowOutputs,
        string location,
        List<string> errors,
        bool allowResponse = false,
        JsonElement? responseSample = null)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return;
        }

        foreach (var token in TemplateResolver.ExtractTokens(template))
        {
            if (token.StartsWith("stage:json(", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryValidateStageJsonToken(token, endpointOutputs, workflowOutputs, out var jsonError))
                {
                    errors.Add($"{jsonError} in {location}.");
                }
                continue;
            }

            if (token.StartsWith("rand:", StringComparison.OrdinalIgnoreCase))
            {
                ValidateRandomToken(token, inputs, globals, environmentVariables, endpointOutputs, workflowOutputs, location, errors);
                continue;
            }

            var segments = TemplateResolver.SplitToken(token);
            if (segments.Length == 0)
            {
                errors.Add($"Invalid token in {location}.");
                continue;
            }

            var root = segments[0].ToLowerInvariant();
            switch (root)
            {
                case "input":
                    if (segments.Length < 2 || !inputs.Contains(segments[1]))
                    {
                        errors.Add($"Unknown input '{token}' in {location}.");
                    }
                    break;
                case "global":
                    if (segments.Length < 2 || !globals.Contains(segments[1]))
                    {
                        errors.Add($"Unknown global '{token}' in {location}.");
                    }
                    break;
                case "endpoint":
                    if (!TryValidateStageOutput(token, segments, endpointOutputs, "endpoint", out var endpointError))
                    {
                        errors.Add($"{endpointError} in {location}.");
                    }
                    break;
                case "workflow":
                    if (!TryValidateStageOutput(token, segments, workflowOutputs, "workflow", out var workflowError))
                    {
                        errors.Add($"{workflowError} in {location}.");
                    }
                    break;
                case "stage":
                    if (TryValidateStageWorkflowResult(token, segments, workflowOutputs, out var workflowResultError))
                    {
                        break;
                    }

                    if (TryValidateStageWorkflowOutput(token, segments, workflowOutputs, out var workflowOutputError))
                    {
                        break;
                    }

                    if (TryValidateStageOutput(token, segments, endpointOutputs, "stage", out var stageEndpointError))
                    {
                        break;
                    }

                    if (TryValidateStageOutput(token, segments, workflowOutputs, "stage", out var stageWorkflowError))
                    {
                        break;
                    }

                    errors.Add($"{stageEndpointError} in {location}.");
                    break;
                case "context":
                    if (segments.Length < 2)
                    {
                        errors.Add($"Invalid context token '{token}' in {location}.");
                    }
                    break;
                case "env":
                    if (segments.Length < 2)
                    {
                        errors.Add($"Invalid env token '{token}' in {location}.");
                        break;
                    }

                    if (!environmentVariables.ContainsKey(segments[1]) &&
                        Environment.GetEnvironmentVariable(segments[1]) is null)
                    {
                        errors.Add($"Environment variable '{segments[1]}' was not defined for token '{token}' in {location}.");
                    }
                    break;
                case "response":
                    if (!allowResponse)
                    {
                        errors.Add($"Response token '{token}' is not allowed in {location}.");
                        break;
                    }

                    if (!TryValidateResponseToken(token, segments, responseSample, out var responseError))
                    {
                        errors.Add($"{responseError} in {location}.");
                    }
                    break;
                case "system":
                    if (segments.Length < 3 ||
                        (!segments[1].Equals("datetime", StringComparison.OrdinalIgnoreCase) &&
                         !segments[1].Equals("date", StringComparison.OrdinalIgnoreCase) &&
                         !segments[1].Equals("time", StringComparison.OrdinalIgnoreCase)) ||
                        (!segments[2].Equals("now", StringComparison.OrdinalIgnoreCase) &&
                         !segments[2].Equals("utcnow", StringComparison.OrdinalIgnoreCase)))
                    {
                        errors.Add($"Invalid token '{token}' in {location}.");
                    }
                    break;
                default:
                    errors.Add($"Unknown token '{token}' in {location}.");
                    break;
            }
        }
    }

    private static bool TryValidateStageJsonToken(
        string token,
        Dictionary<string, HashSet<string>> endpointOutputs,
        Dictionary<string, HashSet<string>> workflowOutputs,
        out string error)
    {
        error = string.Empty;
        const string prefix = "stage:json(";
        if (!token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            error = $"Invalid stage json token '{token}'";
            return false;
        }

        var closeIndex = token.IndexOf(')', prefix.Length);
        if (closeIndex <= prefix.Length)
        {
            error = $"Invalid stage json token '{token}'. Expected closing ')'";
            return false;
        }

        var inner = token[prefix.Length..closeIndex];
        var innerSegments = inner.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (innerSegments.Length < 3 || !innerSegments[1].Equals("output", StringComparison.OrdinalIgnoreCase))
        {
            error = $"Invalid stage json token '{token}'. Expected 'stage:json(<stage>.output.<key>)'";
            return false;
        }

        var segments = new[] { "stage", innerSegments[0], "output", innerSegments[2] };
        if (TryValidateStageOutput(token, segments, endpointOutputs, "stage", out _))
        {
            return true;
        }

        if (TryValidateStageOutput(token, segments, workflowOutputs, "stage", out _))
        {
            return true;
        }

        error = $"Stage outputs were not found for '{token}'";
        return false;
    }

    private static void ValidateRandomToken(
        string token,
        HashSet<string> inputs,
        HashSet<string> globals,
        IReadOnlyDictionary<string, string> environmentVariables,
        Dictionary<string, HashSet<string>> endpointOutputs,
        Dictionary<string, HashSet<string>> workflowOutputs,
        string location,
        List<string> errors)
    {
        var openIndex = token.IndexOf('(');
        var closeIndex = token.LastIndexOf(')');
        if (openIndex <= "rand:".Length || closeIndex != token.Length - 1)
        {
            errors.Add($"Invalid rand token '{token}' in {location}.");
            return;
        }

        var functionName = token["rand:".Length..openIndex].Trim().ToLowerInvariant();
        var arguments = SplitFunctionArguments(token[(openIndex + 1)..closeIndex])
            .Select(argument => argument.Trim())
            .Where(argument => argument.Length > 0)
            .ToArray();

        bool valid = functionName switch
        {
            "number" => ValidateRandomNumberToken(token, arguments, location, errors),
            "text" => ValidateRandomTextToken(token, arguments, location, errors),
            "guid" => ValidateRandomArgumentCount(token, arguments.Length, 0, 0, location, errors, "rand:guid expects no arguments."),
            "ulid" => ValidateRandomArgumentCount(token, arguments.Length, 0, 0, location, errors, "rand:ulid expects no arguments."),
            "date" => ValidateRandomDateToken(token, arguments, location, errors),
            "datetime" => ValidateRandomDateTimeToken(token, arguments, location, errors),
            "time" => ValidateRandomTimeToken(token, arguments, location, errors),
            _ => AddRandomFunctionError(token, location, errors)
        };

        if (!valid)
        {
            return;
        }

        foreach (var argument in arguments)
        {
            if (!LooksLikeRandomTokenReference(argument))
            {
                continue;
            }

            ValidateTemplate(
                $"{{{{{argument}}}}}",
                inputs,
                globals,
                environmentVariables,
                endpointOutputs,
                workflowOutputs,
                location,
                errors,
                allowResponse: false);
        }
    }

    private static bool ValidateRandomArgumentCount(
        string token,
        int count,
        int min,
        int max,
        string location,
        List<string> errors,
        string? detail = null)
    {
        if (count < min || count > max)
        {
            errors.Add(detail is null
                ? $"Invalid rand token '{token}' in {location}."
                : $"Invalid rand token '{token}' in {location}: {detail}");
            return false;
        }

        return true;
    }

    private static bool AddRandomFunctionError(string token, string location, List<string> errors)
    {
        errors.Add($"Unknown rand token '{token}' in {location}.");
        return false;
    }

    private static bool ValidateRandomNumberToken(string token, string[] arguments, string location, List<string> errors)
    {
        if (!ValidateRandomArgumentCount(token, arguments.Length, 0, 2, location, errors, "rand:number expects 0, 1 or 2 integer arguments."))
        {
            return false;
        }

        return ValidateIntegerArgument(arguments, 0, token, location, errors, "minimum") &&
               ValidateIntegerArgument(arguments, 1, token, location, errors, "maximum");
    }

    private static bool ValidateRandomTextToken(string token, string[] arguments, string location, List<string> errors)
    {
        if (!ValidateRandomArgumentCount(token, arguments.Length, 0, 2, location, errors, "rand:text expects length and optional character set."))
        {
            return false;
        }

        if (!ValidateIntegerArgument(arguments, 0, token, location, errors, "length"))
        {
            return false;
        }

        if (arguments.Length < 2 || LooksLikeRandomTokenReference(arguments[1]))
        {
            return true;
        }

        var characterSet = NormalizeRandomLiteral(arguments[1]);
        if (!IsSupportedCharacterSet(characterSet))
        {
            errors.Add($"Invalid rand token '{token}' in {location}: unsupported character set '{characterSet}'. Supported values: alpha, alpha-lower, alpha-upper, alnum, numeric, ascii.");
            return false;
        }

        return true;
    }

    private static bool ValidateRandomDateToken(string token, string[] arguments, string location, List<string> errors)
    {
        if (!ValidateRandomArgumentCount(token, arguments.Length, 0, 3, location, errors, "rand:date expects up to 3 arguments: from, to, format."))
        {
            return false;
        }

        return ValidateDateArgument(arguments, 0, token, location, errors, "from") &&
               ValidateDateArgument(arguments, 1, token, location, errors, "to");
    }

    private static bool ValidateRandomDateTimeToken(string token, string[] arguments, string location, List<string> errors)
    {
        if (!ValidateRandomArgumentCount(token, arguments.Length, 0, 3, location, errors, "rand:datetime expects up to 3 arguments: from, to, format."))
        {
            return false;
        }

        return ValidateDateTimeArgument(arguments, 0, token, location, errors, "from") &&
               ValidateDateTimeArgument(arguments, 1, token, location, errors, "to");
    }

    private static bool ValidateRandomTimeToken(string token, string[] arguments, string location, List<string> errors)
    {
        if (!ValidateRandomArgumentCount(token, arguments.Length, 0, 3, location, errors, "rand:time expects up to 3 arguments: from, to, format."))
        {
            return false;
        }

        return ValidateTimeArgument(arguments, 0, token, location, errors, "from") &&
               ValidateTimeArgument(arguments, 1, token, location, errors, "to");
    }

    private static bool ValidateIntegerArgument(string[] arguments, int index, string token, string location, List<string> errors, string label)
    {
        if (arguments.Length <= index || LooksLikeRandomTokenReference(arguments[index]))
        {
            return true;
        }

        var value = NormalizeRandomLiteral(arguments[index]);
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            errors.Add($"Invalid rand token '{token}' in {location}: {label} '{value}' is not a valid integer.");
            return false;
        }

        return true;
    }

    private static bool ValidateDateArgument(string[] arguments, int index, string token, string location, List<string> errors, string label)
    {
        if (arguments.Length <= index || LooksLikeRandomTokenReference(arguments[index]))
        {
            return true;
        }

        var value = NormalizeRandomLiteral(arguments[index]);
        if (!DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            errors.Add($"Invalid rand token '{token}' in {location}: {label} '{value}' is not a valid date.");
            return false;
        }

        return true;
    }

    private static bool ValidateDateTimeArgument(string[] arguments, int index, string token, string location, List<string> errors, string label)
    {
        if (arguments.Length <= index || LooksLikeRandomTokenReference(arguments[index]))
        {
            return true;
        }

        var value = NormalizeRandomLiteral(arguments[index]);
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
        {
            errors.Add($"Invalid rand token '{token}' in {location}: {label} '{value}' is not a valid datetime.");
            return false;
        }

        return true;
    }

    private static bool ValidateTimeArgument(string[] arguments, int index, string token, string location, List<string> errors, string label)
    {
        if (arguments.Length <= index || LooksLikeRandomTokenReference(arguments[index]))
        {
            return true;
        }

        var value = NormalizeRandomLiteral(arguments[index]);
        if (!TimeOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            errors.Add($"Invalid rand token '{token}' in {location}: {label} '{value}' is not a valid time.");
            return false;
        }

        return true;
    }

    private static string NormalizeRandomLiteral(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '\'' && trimmed[^1] == '\'') ||
             (trimmed[0] == '"' && trimmed[^1] == '"')))
        {
            return trimmed[1..^1];
        }

        return trimmed;
    }

    private static bool IsSupportedCharacterSet(string value)
    {
        return value.Equals("alpha", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("alpha-lower", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("alpha-upper", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("alnum", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("numeric", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("ascii", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeRandomTokenReference(string value)
    {
        return value.StartsWith("input.", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("global.", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("context:", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("context.", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("endpoint:", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("workflow:", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("stage:", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("var:", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("env:", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("system:", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("coalesce(", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> SplitFunctionArguments(string content)
    {
        var depth = 0;
        char? quote = null;
        var start = 0;

        for (var i = 0; i < content.Length; i++)
        {
            if (quote.HasValue)
            {
                if (content[i] == quote.Value && (i == 0 || content[i - 1] != '\\'))
                {
                    quote = null;
                }

                continue;
            }

            switch (content[i])
            {
                case '\'':
                case '"':
                    quote = content[i];
                    break;
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
                case ',' when depth == 0:
                    yield return content[start..i];
                    start = i + 1;
                    break;
            }
        }

        yield return content[start..];
    }

    private static bool TryValidateResponseToken(
        string token,
        string[] segments,
        JsonElement? responseSample,
        out string error)
    {
        error = string.Empty;
        if (segments.Length < 2)
        {
            error = $"Invalid response token '{token}'.";
            return false;
        }

        if (segments[1].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            if (segments.Length != 2)
            {
                error = $"Invalid response token '{token}'. Expected 'response.status'.";
                return false;
            }

            return true;
        }

        if (segments[1].Equals("headers", StringComparison.OrdinalIgnoreCase))
        {
            if (segments.Length != 3)
            {
                error = $"Invalid response token '{token}'. Expected 'response.headers.<HeaderName>'.";
                return false;
            }

            return true;
        }

        if (!responseSample.HasValue)
        {
            return true;
        }

        var path = segments[1].Equals("body", StringComparison.OrdinalIgnoreCase)
            ? segments.Skip(2).ToArray()
            : segments.Skip(1).ToArray();
        if (path.Length == 0)
        {
            return true;
        }

        if (JsonValueHelper.TryResolvePath(responseSample.Value, path, out _))
        {
            return true;
        }

        error = $"Response path '{string.Join(".", path)}' was not found for token '{token}'";
        return false;
    }

    private static bool TryValidateStageOutput(
        string token,
        string[] segments,
        Dictionary<string, HashSet<string>> outputs,
        string kind,
        out string error)
    {
        if (segments.Length < 4 || !segments[2].Equals("output", StringComparison.OrdinalIgnoreCase))
        {
            error = $"Invalid {kind} token '{token}'. Expected '{kind}:<name>.output.<key>'.";
            return false;
        }

        var stageName = segments[1];
        var outputKey = segments[3];
        if (!outputs.TryGetValue(stageName, out var outputKeys))
        {
            error = $"{kind} '{stageName}' outputs were not found";
            return false;
        }

        if (!outputKeys.Contains(outputKey))
        {
            error = $"{kind} '{stageName}' output '{outputKey}' was not found";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryValidateStageWorkflowOutput(
        string token,
        string[] segments,
        Dictionary<string, HashSet<string>> workflowOutputs,
        out string error)
    {
        error = string.Empty;
        if (segments.Length < 5 ||
            !segments[2].Equals("workflow", StringComparison.OrdinalIgnoreCase) ||
            !segments[3].Equals("output", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var stageName = segments[1];
        var outputKey = segments[4];
        if (!workflowOutputs.TryGetValue(stageName, out var outputKeys))
        {
            error = $"stage '{stageName}' outputs were not found";
            return false;
        }

        if (!outputKeys.Contains(outputKey))
        {
            error = $"stage '{stageName}' output '{outputKey}' was not found";
            return false;
        }

        return true;
    }

    private static bool TryValidateStageWorkflowResult(
        string token,
        string[] segments,
        Dictionary<string, HashSet<string>> workflowOutputs,
        out string error)
    {
        error = string.Empty;
        if (segments.Length < 5 ||
            !segments[2].Equals("workflow", StringComparison.OrdinalIgnoreCase) ||
            !segments[3].Equals("result", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var stageName = segments[1];
        if (!workflowOutputs.ContainsKey(stageName))
        {
            error = $"stage '{stageName}' outputs were not found";
            return false;
        }

        var resultKey = segments[4];
        if (!resultKey.Equals("status", StringComparison.OrdinalIgnoreCase) &&
            !resultKey.Equals("message", StringComparison.OrdinalIgnoreCase))
        {
            error = $"stage '{stageName}' workflow result '{resultKey}' was not found";
            return false;
        }

        return true;
    }

    private static void ValidateRunIf(
        string expression,
        HashSet<string> inputs,
        HashSet<string> globals,
        IReadOnlyDictionary<string, string> environmentVariables,
        Dictionary<string, HashSet<string>> endpointOutputs,
        Dictionary<string, HashSet<string>> workflowOutputs,
        string location,
        List<string> errors,
        bool requireExpressionSyntax = true)
    {
        if (requireExpressionSyntax)
        {
            try
            {
                _ = WorkflowExpressionEvaluator.ExtractTemplateTokens(expression);
            }
            catch (Exception ex)
            {
                errors.Add($"Invalid runIf expression '{expression}' in {location}: {ex.Message}");
                return;
            }
        }

        foreach (var token in WorkflowExpressionEvaluator.ExtractTemplateTokens(expression))
        {
            ValidateTemplate($"{{{{{token}}}}}", inputs, globals, environmentVariables, endpointOutputs, workflowOutputs, location, errors, allowResponse: false);
        }
    }

    private string LoadMockPayloadForValidation(
        WorkflowStageDefinition stage,
        string workflowPath,
        IReadOnlyDictionary<string, string> environmentVariables,
        IReadOnlyDictionary<string, string>? runtimeInputs)
    {
        var payloadFile = stage.Mock?.PayloadFile ?? string.Empty;
        if (TryResolvePathForValidation(payloadFile, workflowPath, environmentVariables, runtimeInputs, globals: null, out _, out var resolutionError, out var resolutionWarning))
        {
            return _mockPayloadService.LoadRawPayloadFromFile(
                payloadFile,
                new TemplateContext(
                    runtimeInputs ?? EmptyEnvironmentVariables,
                    EmptyEnvironmentVariables,
                    EmptyEnvironmentVariables,
                    new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase),
                    environmentVariables,
                    WorkflowPath: workflowPath));
        }

        if (resolutionWarning is not null)
        {
            throw new DeferredPathResolutionException(resolutionWarning);
        }

        throw new InvalidOperationException(resolutionError ?? "Mock payload file path could not be resolved.");
    }

    private static Dictionary<string, string> BuildWorkflowLookupForValidation(
        IReadOnlyList<WorkflowReferenceItem>? references,
        string workflowPath,
        IReadOnlyDictionary<string, string> environmentVariables,
        IReadOnlyDictionary<string, string>? runtimeInputs,
        IReadOnlyDictionary<string, string>? globals,
        List<string> errors,
        List<string> warnings)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (references is null)
        {
            return lookup;
        }

        foreach (var reference in references)
        {
            if (string.IsNullOrWhiteSpace(reference.Name))
            {
                errors.Add("Reference name is required.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(reference.Path))
            {
                errors.Add($"Reference '{reference.Name}' path is required.");
                continue;
            }

            if (lookup.ContainsKey(reference.Name))
            {
                errors.Add($"Duplicate reference name '{reference.Name}'.");
                continue;
            }

            if (TryResolvePathForValidation(reference.Path, workflowPath, environmentVariables, runtimeInputs, globals, out var resolvedPath, out var resolutionError, out var resolutionWarning))
            {
                lookup.Add(reference.Name, resolvedPath);
                continue;
            }

            if (resolutionWarning is not null)
            {
                warnings.Add($"Reference '{reference.Name}' path '{reference.Path}' will be resolved at runtime: {resolutionWarning}");
                continue;
            }

            errors.Add($"Reference '{reference.Name}' path '{reference.Path}' could not be resolved: {resolutionError}");
        }

        return lookup;
    }

    private static bool TryResolvePathForValidation(
        string path,
        string workflowPath,
        IReadOnlyDictionary<string, string> environmentVariables,
        IReadOnlyDictionary<string, string>? runtimeInputs,
        IReadOnlyDictionary<string, string>? globals,
        out string resolvedPath,
        out string? error,
        out string? warning)
    {
        try
        {
            resolvedPath = WorkflowReferencePathResolver.ResolvePath(path, workflowPath, environmentVariables, runtimeInputs, globals);
            error = null;
            warning = null;
            return true;
        }
        catch (Exception ex) when (CanSkipPathResolution(ex))
        {
            resolvedPath = string.Empty;
            error = null;
            warning = ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            resolvedPath = string.Empty;
            error = ex.Message;
            warning = null;
            return false;
        }
    }

    private static Dictionary<string, string> BuildValidationGlobals(
        WorkflowInitStage? initStage,
        IReadOnlyDictionary<string, string> environmentVariables,
        IReadOnlyDictionary<string, string>? runtimeInputs)
    {
        var globals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (initStage?.Variables is null || initStage.Variables.Count == 0)
        {
            return globals;
        }

        var resolver = new TemplateResolver();
        foreach (var variable in initStage.Variables)
        {
            if (string.IsNullOrWhiteSpace(variable.Name) || string.IsNullOrWhiteSpace(variable.Value))
            {
                continue;
            }

            try
            {
                var resolvedValue = resolver.ResolveTemplate(
                    variable.Value,
                    new TemplateContext(
                        runtimeInputs ?? EmptyEnvironmentVariables,
                        globals,
                        EmptyEnvironmentVariables,
                        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase),
                        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase),
                        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase),
                        environmentVariables,
                        WorkflowPath: string.Empty));
                globals[variable.Name] = resolvedValue;
            }
            catch
            {
                // Leave unresolved globals deferred during validation.
            }
        }

        return globals;
    }

    private static bool CanSkipPathResolution(Exception ex)
    {
        var message = ex.Message;
        return message.Contains("was not found", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("could not be resolved", StringComparison.OrdinalIgnoreCase);
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

    private void ValidateMockDefinition(
        WorkflowStageDefinition stage,
        string workflowPath,
        HashSet<string> inputs,
        HashSet<string> globals,
        IReadOnlyDictionary<string, string> environmentVariables,
        IReadOnlyDictionary<string, string>? runtimeInputs,
        Dictionary<string, HashSet<string>> endpointOutputs,
        Dictionary<string, HashSet<string>> workflowOutputs,
        List<string> errors,
        List<string> warnings)
    {
        if (stage.Mock?.Status is <= 0)
        {
            errors.Add($"Stage '{stage.Name}' mock status must be a positive integer.");
        }

        if (!WorkflowStageKind.IsWorkflow(stage.Kind))
        {
            if (stage.Mock?.Output is not null && stage.Mock.Output.Count > 0)
            {
                errors.Add($"Stage '{stage.Name}' mock output is not supported for endpoint stages.");
            }

            if (!string.IsNullOrWhiteSpace(stage.Mock?.Payload) && !string.IsNullOrWhiteSpace(stage.Mock?.PayloadFile))
            {
                errors.Add($"Stage '{stage.Name}' mock cannot define both payload and payloadFile.");
                return;
            }

            if (string.IsNullOrWhiteSpace(stage.Mock?.Payload) && string.IsNullOrWhiteSpace(stage.Mock?.PayloadFile))
            {
                errors.Add($"Stage '{stage.Name}' mock payload is required for endpoint stages.");
                return;
            }

            string rawPayload;
            try
            {
                rawPayload = string.IsNullOrWhiteSpace(stage.Mock?.PayloadFile)
                    ? _mockPayloadService.LoadRawPayload(stage.Mock?.Payload ?? string.Empty, workflowPath)
                    : LoadMockPayloadForValidation(stage, workflowPath, environmentVariables, runtimeInputs);
            }
            catch (DeferredPathResolutionException ex)
            {
                warnings.Add($"Stage '{stage.Name}' mock payloadFile '{stage.Mock?.PayloadFile}' will be resolved at runtime: {ex.Message}");
                return;
            }
            catch (Exception ex)
            {
                errors.Add($"Stage '{stage.Name}' mock payload failed to load: {ex.Message}");
                return;
            }

            ValidateTemplate(rawPayload, inputs, globals, environmentVariables, endpointOutputs, workflowOutputs, $"stage '{stage.Name}' mock payload", errors);

            var sanitized = MockPayloadService.SanitizeJsonForValidation(rawPayload);
            if (!MockPayloadService.TryParseJson(sanitized, out var error))
            {
                errors.Add($"Stage '{stage.Name}' mock payload is not valid JSON: {error}");
            }
        }
        else if (WorkflowStageKind.IsWorkflow(stage.Kind))
        {
            if (stage.Mock?.Payload is not null)
            {
                errors.Add($"Stage '{stage.Name}' mock payload is not supported for workflow stages.");
            }

            if (stage.Mock?.Output is null || stage.Mock.Output.Count == 0)
            {
                errors.Add($"Stage '{stage.Name}' mock output is required for workflow stages.");
                return;
            }

            foreach (var output in stage.Mock.Output.Values)
            {
                ValidateTemplate(output, inputs, globals, environmentVariables, endpointOutputs, workflowOutputs, $"stage '{stage.Name}' mock output", errors);
            }
        }
    }

    private JsonElement? TryLoadEndpointMockResponseSample(
        WorkflowStageDefinition stage,
        string workflowPath,
        IReadOnlyDictionary<string, string> environmentVariables,
        IReadOnlyDictionary<string, string>? runtimeInputs)
    {
        if (WorkflowStageKind.IsWorkflow(stage.Kind))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(stage.Mock?.Payload) && string.IsNullOrWhiteSpace(stage.Mock?.PayloadFile))
        {
            return null;
        }

        try
        {
            var rawPayload = string.IsNullOrWhiteSpace(stage.Mock?.PayloadFile)
                ? _mockPayloadService.LoadRawPayload(stage.Mock?.Payload ?? string.Empty, workflowPath)
                : LoadMockPayloadForValidation(stage, workflowPath, environmentVariables, runtimeInputs);
            var sanitized = MockPayloadService.SanitizeJsonForValidation(rawPayload);
            using var document = JsonDocument.Parse(sanitized);
            return document.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }
    private static HashSet<string> BuildApiReferenceLookup(
        IReadOnlyList<ApiReferenceItem>? references,
        List<string> errors)
    {
        var lookup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (references is null)
        {
            return lookup;
        }

        foreach (var reference in references)
        {
            if (string.IsNullOrWhiteSpace(reference.Name))
            {
                errors.Add("API reference name is required.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(reference.Definition))
            {
                errors.Add($"API reference '{reference.Name}' definition is required.");
                continue;
            }

            if (!lookup.Add(reference.Name))
            {
                errors.Add($"Duplicate API reference name '{reference.Name}'.");
            }
        }

        return lookup;
    }

}

public sealed record WorkflowValidationResult(
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

internal sealed class DeferredPathResolutionException : InvalidOperationException
{
    public DeferredPathResolutionException(string message)
        : base(message)
    {
    }
}
