using System;
using System.Collections.Generic;

using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services.Plugins;

namespace SphereIntegrationHub.Services;

internal sealed class WorkflowTemplateValidationStep : IWorkflowValidationStep
{
    public void Validate(WorkflowValidationContext context, List<string> errors)
    {
        ValidateVariableReferences(
            context.Definition,
            context.WorkflowPath,
            context.EnvironmentVariables,
            context.Loader,
            context.MockPayloadService,
            context.StagePlugins,
            errors);
    }

    private static void ValidateVariableReferences(
        WorkflowDefinition definition,
        string workflowPath,
        IReadOnlyDictionary<string, string> environmentVariables,
        WorkflowLoader loader,
        MockPayloadService mockPayloadService,
        StagePluginRegistry stagePlugins,
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

        var workflowRefs = WorkflowReferenceLookupBuilder.BuildWorkflowReferenceLookup(definition.References?.Workflows, workflowPath, errors);

        if (definition.Stages is not null)
        {
            foreach (var stage in definition.Stages)
            {
                if (!stagePlugins.TryGetByKind(stage.Kind, out var plugin))
                {
                    continue;
                }

                if (plugin.Capabilities.OutputKind == StageOutputKind.Endpoint)
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
                else if (plugin.Capabilities.OutputKind == StageOutputKind.Workflow &&
                    !string.IsNullOrWhiteSpace(stage.WorkflowRef))
                {
                    if (workflowRefs.TryGetValue(stage.WorkflowRef, out var referencePath))
                    {
                        try
                        {
                            var nested = loader.Load(referencePath, environmentVariables);
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

                stagePlugins.TryGetByKind(stage.Kind, out var plugin);
                var allowResponse = plugin?.Capabilities.AllowsResponseTokens ?? false;

                if (!string.IsNullOrWhiteSpace(stage.Message))
                {
                    ValidateTemplate(stage.Message, inputNames, globalNames, environmentVariables, endpointOutputs, workflowOutputs, $"stage '{stage.Name}' message", errors, allowResponse: allowResponse);
                }

                if (stage.Output is not null)
                {
                    foreach (var output in stage.Output.Values)
                    {
                        ValidateTemplate(output, inputNames, globalNames, environmentVariables, endpointOutputs, workflowOutputs, $"stage '{stage.Name}' output", errors, allowResponse: allowResponse);
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
                    if (plugin is null)
                    {
                        errors.Add($"Stage '{stage.Name}' mock is defined but no plugin was loaded for kind '{stage.Kind}'.");
                    }
                    else
                    {
                        ValidateMockDefinition(stage, plugin, workflowPath, inputNames, globalNames, environmentVariables, endpointOutputs, workflowOutputs, mockPayloadService, errors);
                    }
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

    private static void ValidateMockDefinition(
        WorkflowStageDefinition stage,
        IStagePlugin plugin,
        string workflowPath,
        HashSet<string> inputs,
        HashSet<string> globals,
        IReadOnlyDictionary<string, string> environmentVariables,
        Dictionary<string, HashSet<string>> endpointOutputs,
        Dictionary<string, HashSet<string>> workflowOutputs,
        MockPayloadService mockPayloadService,
        List<string> errors)
    {
        if (stage.Mock?.Status is <= 0)
        {
            errors.Add($"Stage '{stage.Name}' mock status must be a positive integer.");
        }

        if (plugin.Capabilities.MockKind == StageMockKind.None)
        {
            errors.Add($"Stage '{stage.Name}' mock is not supported for kind '{stage.Kind}'.");
            return;
        }

        if (plugin.Capabilities.MockKind == StageMockKind.Endpoint)
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
                    ? mockPayloadService.LoadRawPayload(stage.Mock?.Payload ?? string.Empty, workflowPath)
                    : mockPayloadService.LoadRawPayloadFromFile(stage.Mock?.PayloadFile ?? string.Empty, workflowPath);
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
        else if (plugin.Capabilities.MockKind == StageMockKind.Workflow)
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
}
