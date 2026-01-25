using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.Services;

public sealed class WorkflowValidator
{
    private readonly WorkflowLoader _loader;
    private readonly MockPayloadService _mockPayloadService;

    public WorkflowValidator(WorkflowLoader loader)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _mockPayloadService = new MockPayloadService();
    }

    public IReadOnlyList<string> Validate(WorkflowDocument document)
    {
        using var activity = Telemetry.ActivitySource.StartActivity(TelemetryConstants.ActivityWorkflowValidate);
        activity?.SetTag(TelemetryConstants.TagWorkflowName, document.Definition.Name);
        var errors = new List<string>();
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
        ValidateStages(definition, document.FilePath, document.EnvironmentVariables, errors);
        ValidateEndStage(definition, errors);
        ValidateVariableReferences(definition, document.FilePath, document.EnvironmentVariables, errors);

        return errors;
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

    private void ValidateStages(
        WorkflowDefinition definition,
        string workflowPath,
        IReadOnlyDictionary<string, string> environmentVariables,
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
        var workflowLookup = BuildWorkflowReferenceLookup(references?.Workflows, workflowPath, errors);
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
                    var referencedWorkflow = _loader.Load(referencePath, environmentVariables);
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

    private void ValidateVariableReferences(
        WorkflowDefinition definition,
        string workflowPath,
        IReadOnlyDictionary<string, string> environmentVariables,
        List<string> errors)
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

        var workflowRefs = BuildWorkflowReferenceLookup(definition.References?.Workflows, workflowPath, errors);

        if (definition.Stages is not null)
        {
            foreach (var stage in definition.Stages)
            {
                if (stage.Kind == WorkflowStageKind.Endpoint)
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
                else if (stage.Kind == WorkflowStageKind.Workflow && !string.IsNullOrWhiteSpace(stage.WorkflowRef))
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

                if (stage.Inputs is not null)
                {
                    foreach (var input in stage.Inputs.Values)
                    {
                        ValidateTemplate(input, inputNames, globalNames, environmentVariables, endpointOutputs, workflowOutputs, $"stage '{stage.Name}' input", errors);
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
                    ValidateTemplate(stage.Message, inputNames, globalNames, environmentVariables, endpointOutputs, workflowOutputs, $"stage '{stage.Name}' message", errors, allowResponse: stage.Kind == WorkflowStageKind.Endpoint);
                }

                if (stage.Output is not null)
                {
                    foreach (var output in stage.Output.Values)
                    {
                        ValidateTemplate(output, inputNames, globalNames, environmentVariables, endpointOutputs, workflowOutputs, $"stage '{stage.Name}' output", errors, allowResponse: stage.Kind == WorkflowStageKind.Endpoint);
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

                if (stage.Mock is not null)
                {
                    ValidateMockDefinition(stage, workflowPath, inputNames, globalNames, environmentVariables, endpointOutputs, workflowOutputs, errors);
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
        bool allowResponse = false)
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
        List<string> errors)
    {
        if (!RunIfParser.TryParse(expression, out var token, out _, out _))
        {
            errors.Add($"Invalid runIf expression '{expression}' in {location}.");
            return;
        }

        ValidateTemplate($"{{{{{token}}}}}", inputs, globals, environmentVariables, endpointOutputs, workflowOutputs, location, errors, allowResponse: false);
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
        Dictionary<string, HashSet<string>> endpointOutputs,
        Dictionary<string, HashSet<string>> workflowOutputs,
        List<string> errors)
    {
        if (stage.Mock?.Status is <= 0)
        {
            errors.Add($"Stage '{stage.Name}' mock status must be a positive integer.");
        }

        if (stage.Kind == WorkflowStageKind.Endpoint)
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
                    : _mockPayloadService.LoadRawPayloadFromFile(stage.Mock?.PayloadFile ?? string.Empty, workflowPath);
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
        else if (stage.Kind == WorkflowStageKind.Workflow)
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

    private static Dictionary<string, string> BuildWorkflowReferenceLookup(
        IReadOnlyList<WorkflowReferenceItem>? references,
        string workflowPath,
        List<string> errors)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (references is null)
        {
            return lookup;
        }

        var baseDirectory = Path.GetDirectoryName(workflowPath) ?? string.Empty;
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

            var resolvedPath = Path.GetFullPath(Path.Combine(baseDirectory, reference.Path));
            lookup.Add(reference.Name, resolvedPath);
        }

        return lookup;
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
