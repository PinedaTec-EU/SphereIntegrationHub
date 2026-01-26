using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services.Interfaces;
using SphereIntegrationHub.Services.Plugins;

namespace SphereIntegrationHub.Services;

public sealed class WorkflowExecutor
{
    private readonly DynamicValueService _dynamicValueService;
    private readonly TemplateResolver _templateResolver;
    private readonly RandomValueFormattingOptions _formatting;
    private readonly MockPayloadService _mockPayloadService;
    private readonly WorkflowLoader _workflowLoader;
    private readonly VarsFileLoader _varsFileLoader;
    private readonly ISystemTimeProvider _systemProvider;
    private readonly IEndpointInvoker _endpointInvoker;
    private readonly IWorkflowOutputWriter _outputWriter;
    private readonly IExecutionLogger _logger;
    private readonly StageMessageEmitter _stageMessageEmitter;
    private readonly IRunIfEvaluator _runIfEvaluator;
    private readonly StagePluginRegistry _stagePlugins;

    public WorkflowExecutor(
        HttpClient httpClient,
        DynamicValueService dynamicValueService,
        StagePluginRegistry stagePlugins,
        WorkflowLoader? workflowLoader = null,
        VarsFileLoader? varsFileLoader = null,
        TemplateResolver? templateResolver = null,
        MockPayloadService? mockPayloadService = null,
        RandomValueFormattingOptions? formatting = null,
        ISystemTimeProvider? systemProvider = null,
        IEndpointInvoker? endpointInvoker = null,
        IWorkflowOutputWriter? outputWriter = null,
        IExecutionLogger? logger = null)
    {
        _dynamicValueService = dynamicValueService ?? throw new ArgumentNullException(nameof(dynamicValueService));
        _workflowLoader = workflowLoader ?? new WorkflowLoader();
        _varsFileLoader = varsFileLoader ?? new VarsFileLoader();
        _systemProvider = systemProvider ?? new SystemTimeProvider();
        _templateResolver = templateResolver ?? new TemplateResolver(_systemProvider);
        _mockPayloadService = mockPayloadService ?? new MockPayloadService();
        _formatting = formatting ?? RandomValueFormattingOptions.Default;
        _endpointInvoker = endpointInvoker ?? new HttpEndpointInvoker(httpClient, _templateResolver);
        _outputWriter = outputWriter ?? new WorkflowOutputWriter();
        _logger = logger ?? new ConsoleExecutionLogger();
        _stageMessageEmitter = new StageMessageEmitter(_templateResolver, _logger);
        _runIfEvaluator = new RunIfEvaluator(_systemProvider);
        _stagePlugins = stagePlugins ?? throw new ArgumentNullException(nameof(stagePlugins));
    }

    public async Task<WorkflowExecutionResult> ExecuteAsync(
        WorkflowDocument document,
        ApiCatalogVersion catalogVersion,
        string environment,
        IReadOnlyDictionary<string, string> inputs,
        bool varsOverrideActive,
        bool mocked,
        bool verbose,
        bool debug,
        CancellationToken cancellationToken)
    {
        var context = new ExecutionContext(inputs, document.EnvironmentVariables);
        await ExecuteWorkflowAsync(
            document,
            catalogVersion,
            environment,
            context,
            varsOverrideActive,
            mocked,
            verbose,
            debug,
            cancellationToken,
            captureErrors: false);
        return new WorkflowExecutionResult(context.WorkflowOutputs[document.Definition.Name], context.OutputFilePath);
    }

    private async Task<WorkflowExecutionOutcome> ExecuteWorkflowAsync(
        WorkflowDocument document,
        ApiCatalogVersion catalogVersion,
        string environment,
        ExecutionContext context,
        bool varsOverrideActive,
        bool mocked,
        bool verbose,
        bool debug,
        CancellationToken cancellationToken,
        bool captureErrors)
    {
        var workflowTimer = Stopwatch.StartNew();
        var definition = document.Definition;
        using var workflowActivity = Telemetry.ActivitySource.StartActivity(TelemetryConstants.ActivityWorkflowExecute);
        workflowActivity?.SetTag(TelemetryConstants.TagWorkflowName, definition.Name);
        workflowActivity?.SetTag(TelemetryConstants.TagWorkflowId, definition.Id);
        workflowActivity?.SetTag(TelemetryConstants.TagWorkflowVersion, definition.Version);
        var indent = ExecutionLogFormatter.GetIndent(context);
        var stageContext = new StageExecutionContext(
            document,
            catalogVersion,
            environment,
            context,
            _templateResolver,
            _mockPayloadService,
            _workflowLoader,
            _varsFileLoader,
            _systemProvider,
            _endpointInvoker,
            _logger,
            _stageMessageEmitter,
            (nestedDocument, nestedContext, token) => ExecuteWorkflowAsync(
                nestedDocument,
                catalogVersion,
                environment,
                nestedContext,
                varsOverrideActive,
                mocked,
                verbose,
                debug,
                token,
                captureErrors: true),
            varsOverrideActive,
            mocked,
            verbose,
            debug);

        try
        {
            if (!mocked)
            {
                ValidateInputs(definition, context.Inputs);
            }
            InitializeGlobals(definition, context);
            _logger.Info($"{indent}{ExecutionLogFormatter.FormatWorkflowTag(definition.Name)}#initStage processed.");

            if (definition.Stages is not null)
            {
                var stageIndex = definition.Stages
                    .Select((stage, index) => new { stage.Name, Index = index })
                    .ToDictionary(item => item.Name, item => item.Index, StringComparer.OrdinalIgnoreCase);

                var index = 0;
                while (index < definition.Stages.Count)
                {
                    var stage = definition.Stages[index];
                    if (!_runIfEvaluator.ShouldRunStage(stage, context))
                    {
                        index++;
                        continue;
                    }

                    await ApplyStageDelayAsync(definition.Name, stage, context, verbose, cancellationToken);

                    if (debug)
                    {
                        PrintStageDebug(definition, stage, context);
                    }

                    if (!_stagePlugins.TryGetByKind(stage.Kind, out var plugin))
                    {
                        throw new InvalidOperationException($"Stage '{stage.Name}' kind '{stage.Kind}' does not match any loaded plugin.");
                    }

                    using var stageActivity = Telemetry.ActivitySource.StartActivity(TelemetryConstants.ActivityWorkflowStage);
                    stageActivity?.SetTag(TelemetryConstants.TagStageName, stage.Name);
                    stageActivity?.SetTag(TelemetryConstants.TagStageKind, stage.Kind);
                    if (verbose)
                    {
                        _logger.Info($"{indent}{ExecutionLogFormatter.FormatStageTag(definition.Name, stage.Name)} started [{stage.Kind}].");
                    }

                    var stageTimer = Stopwatch.StartNew();
                    string? jumpTarget = null;
                    try
                    {
                        jumpTarget = await plugin.ExecuteAsync(stage, stageContext, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        stageActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                        _logger.Error($"{indent}{ExecutionLogFormatter.FormatStageTag(definition.Name, stage.Name)} failed after {stageTimer.Elapsed.TotalMilliseconds:F0} ms: {ex.Message}");
                        if (plugin.Capabilities.ContinueOnError)
                        {
                            ApplyWorkflowStageResult(context, stage.Name, WorkflowResultStatus.Error, ex.Message);
                        }
                        else
                        {
                            if (captureErrors)
                            {
                                return BuildWorkflowErrorResult(definition, context, ex);
                            }

                            throw;
                        }
                    }
                    finally
                    {
                        stageTimer.Stop();
                    }

                    _logger.Info($"{indent}{ExecutionLogFormatter.FormatStageTag(definition.Name, stage.Name)} completed in {stageTimer.Elapsed.TotalMilliseconds:F0} ms.");
                    ApplyStageSetters(stage, context);
                    if (plugin.Capabilities.SupportsJumpOnStatus && !string.IsNullOrWhiteSpace(jumpTarget))
                    {
                        if (verbose)
                        {
                            _logger.Info($"{indent}{ExecutionLogFormatter.FormatStageTag(definition.Name, stage.Name)} jump target: {jumpTarget}.");
                        }

                        if (string.Equals(jumpTarget, "endStage", StringComparison.OrdinalIgnoreCase))
                        {
                            break;
                        }

                        if (string.Equals(jumpTarget, stage.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            if (mocked)
                            {
                                throw new MockedSelfJumpException(definition.Name, stage.Name, jumpTarget);
                            }

                            if (!ConfirmSelfJump(definition.Name, stage.Name))
                            {
                                throw new InvalidOperationException(
                                    $"Stage '{stage.Name}' jumpOnStatus targets itself. Confirmation was not granted.");
                            }
                        }

                        if (stageIndex.TryGetValue(jumpTarget, out var nextIndex))
                        {
                            index = nextIndex;
                            continue;
                        }
                    }

                    index++;
                }
            }

            var workflowOutput = ResolveWorkflowOutput(definition, context);
            context.WorkflowOutputs[definition.Name] = workflowOutput;

            ApplyEndStageContext(definition, context);
            _logger.Info($"{indent}{ExecutionLogFormatter.FormatWorkflowTag(definition.Name)}#endStage processed.");

            if (definition.Output)
            {
                context.OutputFilePath = await _outputWriter.WriteOutputAsync(
                    definition,
                    document,
                    workflowOutput,
                    cancellationToken);
            }

            workflowTimer.Stop();
            _logger.Info($"{indent}Workflow {ExecutionLogFormatter.FormatWorkflowTag(definition.Name)} completed in {workflowTimer.Elapsed.TotalMilliseconds:F0} ms.");
            return BuildWorkflowSuccessResult(definition, context);
        }
        catch (Exception ex)
        {
            workflowActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            if (!captureErrors)
            {
                throw;
            }

            return BuildWorkflowErrorResult(definition, context, ex);
        }
    }


    private async Task ApplyStageDelayAsync(
        string workflowName,
        WorkflowStageDefinition stage,
        ExecutionContext context,
        bool verbose,
        CancellationToken cancellationToken)
    {
        var delaySeconds = stage.DelaySeconds ?? 0;
        if (delaySeconds <= 0)
        {
            return;
        }

        if (verbose)
        {
            _logger.Info($"{ExecutionLogFormatter.GetIndent(context)}{ExecutionLogFormatter.FormatStageTag(workflowName, stage.Name)} delay: {delaySeconds}s.");
        }

        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
    }


    private void InitializeGlobals(WorkflowDefinition definition, ExecutionContext context)
    {
        if (definition.InitStage?.Variables is null)
        {
            ApplyInitContext(definition, context);
            return;
        }

        foreach (var variable in definition.InitStage.Variables)
        {
            var resolvedValue = variable.Value is null
                ? null
                : _templateResolver.ResolveTemplate(variable.Value, context.BuildTemplateContext());

            var randomDefinition = new RandomValueDefinition(
                variable.Type,
                resolvedValue,
                variable.Min,
                variable.Max,
                variable.Padding,
                variable.Length,
                variable.FromDateTime,
                variable.ToDateTime,
                variable.FromDate,
                variable.ToDate,
                variable.FromTime,
                variable.ToTime,
                variable.Format,
                variable.Start,
                variable.Step);

            var value = _dynamicValueService.Generate(randomDefinition, new PayloadProcessorContext(1, string.Empty, string.Empty, string.Empty, string.Empty), _formatting);
            context.Globals[variable.Name] = value;
        }

        ApplyInitContext(definition, context);
    }

    private void ApplyInitContext(WorkflowDefinition definition, ExecutionContext context)
    {
        if (definition.InitStage?.Context is null)
        {
            return;
        }

        foreach (var pair in definition.InitStage.Context)
        {
            if (context.Context.ContainsKey(pair.Key))
            {
                continue;
            }

            var resolved = _templateResolver.ResolveTemplate(pair.Value, context.BuildTemplateContext());
            context.Context[pair.Key] = resolved;
        }
    }

    private static void ValidateInputs(WorkflowDefinition definition, IReadOnlyDictionary<string, string> inputs)
    {
        if (definition.Input is null)
        {
            return;
        }

        foreach (var input in definition.Input)
        {
            if (input.Required && !inputs.ContainsKey(input.Name))
            {
                throw new InvalidOperationException($"Required input '{input.Name}' was not provided.");
            }
        }
    }

    private IReadOnlyDictionary<string, string> ResolveWorkflowOutput(WorkflowDefinition definition, ExecutionContext context)
    {
        var output = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (definition.EndStage?.Output is null)
        {
            return output;
        }

        foreach (var pair in definition.EndStage.Output)
        {
            var resolved = _templateResolver.ResolveTemplate(pair.Value, context.BuildTemplateContext());
            output[pair.Key] = resolved;
        }

        return output;
    }

    private WorkflowExecutionOutcome BuildWorkflowSuccessResult(WorkflowDefinition definition, ExecutionContext context)
    {
        var message = string.Empty;
        var template = definition.EndStage?.Result?.Message;
        if (!string.IsNullOrWhiteSpace(template))
        {
            message = _templateResolver.ResolveTemplate(template, context.BuildTemplateContext());
        }

        return ApplyWorkflowResult(definition.Name, context, WorkflowResultStatus.Ok.ToString(), message);
    }

    private WorkflowExecutionOutcome BuildWorkflowErrorResult(WorkflowDefinition definition, ExecutionContext context, Exception ex)
    {
        return ApplyWorkflowResult(definition.Name, context, WorkflowResultStatus.Error.ToString(), ex.Message);
    }

    private WorkflowExecutionOutcome ApplyWorkflowResult(
        string workflowName,
        ExecutionContext context,
        string status,
        string message)
    {
        var result = new WorkflowExecutionOutcome(status, message);
        context.WorkflowResults[workflowName] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["status"] = result.Status,
            ["message"] = result.Message
        };
        return result;
    }

    private void ApplyWorkflowStageResult(
        ExecutionContext context,
        string stageName,
        WorkflowResultStatus status,
        string message)
    {
        ApplyWorkflowStageResult(context, stageName, status.ToString(), message);
    }

    private void ApplyWorkflowStageResult(
        ExecutionContext context,
        string stageName,
        string status,
        string message)
    {
        context.WorkflowResults[stageName] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["status"] = status,
            ["message"] = message
        };
    }

    private void ApplyStageSetters(WorkflowStageDefinition stage, ExecutionContext context)
    {
        if (stage.Set is not null)
        {
            foreach (var pair in stage.Set)
            {
                var resolved = _templateResolver.ResolveTemplate(pair.Value, context.BuildTemplateContext());
                context.Globals[pair.Key] = resolved;
            }
        }

        if (stage.Context is not null)
        {
            foreach (var pair in stage.Context)
            {
                var resolved = _templateResolver.ResolveTemplate(pair.Value, context.BuildTemplateContext());
                context.Context[pair.Key] = resolved;
            }
        }
    }

    private static bool ConfirmSelfJump(string workflowName, string stageName)
    {
        if (Console.IsInputRedirected)
        {
            throw new InvalidOperationException(
                $"Self-jump confirmation required for stage '{stageName}', but input is redirected.");
        }

        Console.Out.Write($"Stage '{workflowName}/{stageName}' will jump to itself. Type 'yes' to continue: ");
        var response = Console.In.ReadLine();
        return string.Equals(response?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyEndStageContext(WorkflowDefinition definition, ExecutionContext context)
    {
        if (definition.EndStage?.Context is null)
        {
            return;
        }

        foreach (var pair in definition.EndStage.Context)
        {
            var resolved = _templateResolver.ResolveTemplate(pair.Value, context.BuildTemplateContext());
            context.Context[pair.Key] = resolved;
        }
    }

    private void PrintStageDebug(WorkflowDefinition definition, WorkflowStageDefinition stage, ExecutionContext context)
    {
        if (stage.Debug is null || stage.Debug.Count == 0)
        {
            return;
        }

        var indent = ExecutionLogFormatter.GetIndent(context);
        _logger.Info($"{indent}{ExecutionLogFormatter.FormatStageTag(definition.Name, stage.Name)} debug:");
        var templateContext = context.BuildTemplateContext();
        foreach (var pair in stage.Debug)
        {
            var resolved = _templateResolver.ResolveTemplate(pair.Value, templateContext);
            _logger.Info($"{indent} {pair.Key}: {resolved}");
        }
    }

}

public sealed record WorkflowExecutionResult(
    IReadOnlyDictionary<string, string> Output,
    string? OutputFilePath);
