using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;
using SphereIntegrationHub.Services.Interfaces;

namespace SphereIntegrationHub.Services.Plugins;

internal sealed class WorkflowStagePlugin : IStagePlugin, IStageValidator
{
    public string Id => "workflow";

    public IReadOnlyCollection<string> StageKinds { get; } = new[]
    {
        WorkflowStageKinds.Workflow
    };

    public StagePluginCapabilities Capabilities { get; } = new(
        StageOutputKind.Workflow,
        StageMockKind.Workflow,
        AllowsResponseTokens: false,
        SupportsJumpOnStatus: false,
        ContinueOnError: true);

    public void ValidateStage(WorkflowStageDefinition stage, StageValidationContext context, List<string> errors)
    {
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
            return;
        }

        if (!context.WorkflowReferences.TryGetValue(stage.WorkflowRef, out var referencePath))
        {
            errors.Add($"Stage '{stage.Name}' workflowRef '{stage.WorkflowRef}' is not declared in references.");
            return;
        }

        if (!File.Exists(referencePath))
        {
            errors.Add($"Referenced workflow '{stage.WorkflowRef}' was not found at '{referencePath}'.");
            return;
        }

        try
        {
            var referencedWorkflow = context.Loader.Load(referencePath, context.EnvironmentVariables);
            ValidateWorkflowStageInputs(stage, referencedWorkflow.Definition, errors);
            ValidateWorkflowVersion(stage, context.Definition.Version, referencedWorkflow.Definition.Version, errors);
        }
        catch (Exception ex)
        {
            errors.Add($"Referenced workflow '{stage.WorkflowRef}' failed to load: {ex.Message}");
        }
    }

    public async Task<string?> ExecuteAsync(WorkflowStageDefinition stage, StageExecutionContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stage.WorkflowRef))
        {
            throw new InvalidOperationException($"Stage '{stage.Name}' workflowRef is required.");
        }

        var reference = context.Definition.References?.Workflows?.FirstOrDefault(item =>
            string.Equals(item.Name, stage.WorkflowRef, StringComparison.OrdinalIgnoreCase));
        if (reference is null)
        {
            throw new InvalidOperationException($"Workflow reference '{stage.WorkflowRef}' was not found.");
        }

        var nestedPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(context.Document.FilePath) ?? string.Empty, reference.Path));
        var nestedDocument = context.WorkflowLoader.Load(nestedPath, context.Execution.EnvironmentVariables);
        if (context.Verbose)
        {
            context.Logger.Info($"{ExecutionLogFormatter.GetIndent(context.Execution)}{ExecutionLogFormatter.FormatStageTag(context.Definition.Name, stage.Name)} resolved workflow '{nestedDocument.Definition.Name}' at '{nestedDocument.FilePath}'.");
        }

        context.Logger.Info($"{ExecutionLogFormatter.GetIndent(context.Execution)}Calling nested workflow {ExecutionLogFormatter.FormatWorkflowTag(nestedDocument.Definition.Name)} from stage {ExecutionLogFormatter.FormatStageTag(context.Definition.Name, stage.Name)}.");
        if (context.Verbose)
        {
            context.Logger.Info($"{ExecutionLogFormatter.GetIndent(context.Execution.IndentLevel + 1)}Workflow loaded: {nestedDocument.Definition.Name} ({nestedDocument.Definition.Id}).");
        }

        if (context.Mocked && stage.Mock is not null)
        {
            ApplyWorkflowMock(stage, context);
            ApplyWorkflowStageResult(context, stage.Name, WorkflowResultStatus.Ok.ToString(), string.Empty);
            context.StageMessageEmitter.Emit(context.Definition, stage, context.Execution, null);
            return null;
        }

        var nestedInputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var inputs = stage.Inputs;
        var hasStageInputs = inputs is not null && inputs.Count > 0;
        if (hasStageInputs)
        {
            foreach (var pair in inputs!)
            {
                var resolved = context.TemplateResolver.ResolveTemplate(pair.Value, context.Execution.BuildTemplateContext());
                nestedInputs[pair.Key] = resolved;
            }
        }

        var nestedVarsPath = ResolveAutoVarsFilePath(nestedDocument.FilePath);
        if (nestedVarsPath is not null)
        {
            if (context.VarsOverrideActive)
            {
                context.Logger.Info($"{ExecutionLogFormatter.GetIndent(context.Execution.IndentLevel + 1)}Vars file: overrided by main workflow");
            }
            else if (!hasStageInputs)
            {
                try
                {
                    var varsResolution = context.VarsFileLoader.LoadWithDetails(nestedVarsPath, context.Environment, nestedDocument.Definition.Version);
                    nestedInputs.Clear();
                    foreach (var pair in varsResolution.Values)
                    {
                        nestedInputs[pair.Key] = pair.Value;
                    }

                    context.Logger.Info($"{ExecutionLogFormatter.GetIndent(context.Execution.IndentLevel + 1)}Vars file: {nestedVarsPath} (auto)");
                    if (context.Verbose)
                    {
                        LogVarsSources(varsResolution, context.Logger, context.Execution.IndentLevel + 2);
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Failed to load vars file [{nestedVarsPath}] for workflow '{nestedDocument.Definition.Name}': {ex.Message}",
                        ex);
                }
            }
        }

        var nestedContext = new ExecutionContext(
            nestedInputs,
            nestedDocument.EnvironmentVariables,
            context.Execution.Context,
            context.Execution.IndentLevel + 1);
        var nestedResult = await context.ExecuteNestedWorkflowAsync(nestedDocument, nestedContext, cancellationToken);
        if (nestedContext.WorkflowOutputs.TryGetValue(nestedDocument.Definition.Name, out var nestedOutput))
        {
            context.Execution.WorkflowOutputs[stage.Name] = nestedOutput;
        }
        else
        {
            context.Execution.WorkflowOutputs[stage.Name] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        ApplyWorkflowStageResult(context, stage.Name, nestedResult.Status, nestedResult.Message);
        context.StageMessageEmitter.Emit(context.Definition, stage, context.Execution, null);
        context.Logger.Info(string.Empty);
        return null;
    }

    private static void ApplyWorkflowStageResult(
        StageExecutionContext context,
        string stageName,
        string status,
        string message)
    {
        context.Execution.WorkflowResults[stageName] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["status"] = status,
            ["message"] = message
        };
    }

    private static string? ResolveAutoVarsFilePath(string workflowPath)
    {
        var directory = Path.GetDirectoryName(workflowPath) ?? string.Empty;
        var workflowName = Path.GetFileNameWithoutExtension(workflowPath);
        if (string.IsNullOrWhiteSpace(workflowName))
        {
            return null;
        }

        var defaultPath = Path.Combine(directory, $"{workflowName}.wfvars");
        return File.Exists(defaultPath) ? defaultPath : null;
    }

    private static void LogVarsSources(VarsFileResolution resolution, IExecutionLogger logger, int indentLevel)
    {
        var indent = ExecutionLogFormatter.GetIndent(indentLevel);
        if (resolution.Sources.Count == 0)
        {
            logger.Info($"{indent}Vars file variable sources: (none)");
            return;
        }

        logger.Info($"{indent}Vars file variable sources:");
        foreach (var pair in resolution.Sources.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            logger.Info($"{indent}  {pair.Key}: {FormatVarsSource(pair.Value)}");
        }
    }

    private static string FormatVarsSource(VarsFileSource source)
    {
        return source.Scope switch
        {
            "global" => "global",
            "environment" => source.Environment is null ? "environment" : $"environment {source.Environment}",
            "version" => source.Version is null || source.Environment is null
                ? "version"
                : $"environment {source.Environment} / version {source.Version}",
            _ => source.Scope
        };
    }

    private static void ApplyWorkflowMock(WorkflowStageDefinition stage, StageExecutionContext context)
    {
        if (stage.Mock?.Output is null || stage.Mock.Output.Count == 0)
        {
            throw new InvalidOperationException($"Stage '{stage.Name}' mock output is required for workflow stages.");
        }

        var output = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in stage.Mock.Output)
        {
            var resolved = context.TemplateResolver.ResolveTemplate(pair.Value, context.Execution.BuildTemplateContext());
            output[pair.Key] = resolved;
        }

        context.Execution.WorkflowOutputs[stage.Name] = output;
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
}
