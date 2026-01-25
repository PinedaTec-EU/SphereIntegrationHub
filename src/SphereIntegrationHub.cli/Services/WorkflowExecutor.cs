using System.Diagnostics;
using System.IO;
using System.Text.Json;

using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services.Interfaces;

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

    public WorkflowExecutor(
        HttpClient httpClient,
        DynamicValueService dynamicValueService,
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
    }

    private static string FormatWorkflowTag(string name) => $"[{name}]";

    private static string FormatStageTag(string workflowName, string stageName)
        => $"{FormatWorkflowTag(workflowName)}#{stageName}";

    private static string GetIndent(int indentLevel)
        => indentLevel <= 0 ? string.Empty : new string(' ', indentLevel);

    private static string GetIndent(ExecutionContext context) => GetIndent(context.IndentLevel);

    private sealed record RetryPolicy(
        int MaxRetries,
        int DelayMs,
        IReadOnlyList<int> HttpStatus,
        string? OnExceptionMessage);

    private sealed record CircuitBreakerPolicy(
        string Name,
        int FailureThreshold,
        int BreakMs,
        IReadOnlyList<int> HttpStatus,
        string? OnOpenMessage,
        string? OnBlockedMessage,
        int CloseOnSuccessAttempts);

    private sealed class CircuitBreakerState
    {
        public int ConsecutiveFailures { get; set; }
        public int ConsecutiveSuccesses { get; set; }
        public DateTimeOffset? OpenUntil { get; set; }
        public bool HalfOpen { get; set; }
    }

    private sealed record WorkflowResultInfo(string Status, string Message);

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

    private async Task<WorkflowResultInfo> ExecuteWorkflowAsync(
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
        var apiBaseUrls = BuildApiBaseUrlLookup(definition, catalogVersion, environment);
        var indent = GetIndent(context);

        try
        {
            if (!mocked)
            {
                ValidateInputs(definition, context.Inputs);
            }
            InitializeGlobals(definition, context);
            _logger.Info($"{indent}{FormatWorkflowTag(definition.Name)}#initStage processed.");

        if (definition.Stages is not null)
        {
            var stageIndex = definition.Stages
                .Select((stage, index) => new { stage.Name, Index = index })
                .ToDictionary(item => item.Name, item => item.Index, StringComparer.OrdinalIgnoreCase);

            var index = 0;
            while (index < definition.Stages.Count)
            {
                var stage = definition.Stages[index];
                if (!ShouldRunStage(stage, context))
                {
                    index++;
                    continue;
                }

                await ApplyStageDelayAsync(definition.Name, stage, context, verbose, cancellationToken);

                if (debug)
                {
                    PrintStageDebug(definition, stage, context);
                }

                if (stage.Kind == WorkflowStageKind.Workflow)
                {
                    using var stageActivity = Telemetry.ActivitySource.StartActivity(TelemetryConstants.ActivityWorkflowStage);
                    stageActivity?.SetTag(TelemetryConstants.TagStageName, stage.Name);
                    stageActivity?.SetTag(TelemetryConstants.TagStageKind, stage.Kind.ToString());
                    if (verbose)
                    {
                        _logger.Info($"{indent}{FormatStageTag(definition.Name, stage.Name)} started [Workflow].");
                    }

                    var stageTimer = Stopwatch.StartNew();
                    try
                    {
                        if (mocked && stage.Mock is not null)
                        {
                            ApplyWorkflowMock(stage, context);
                            ApplyWorkflowStageResult(context, stage.Name, WorkflowResultStatus.Ok, string.Empty);
                        }
                        else
                        {
                            await ExecuteWorkflowStageAsync(
                                document,
                                stage,
                                catalogVersion,
                                environment,
                                context,
                                varsOverrideActive,
                                mocked,
                                verbose,
                                debug,
                                cancellationToken);
                        }
                        PrintStageMessage(definition, stage, context, null);
                    }
                    catch (Exception ex)
                    {
                        stageActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                        _logger.Error($"{indent}{FormatStageTag(definition.Name, stage.Name)} failed after {stageTimer.Elapsed.TotalMilliseconds:F0} ms: {ex.Message}");
                        ApplyWorkflowStageResult(context, stage.Name, WorkflowResultStatus.Error, ex.Message);
                    }
                    finally
                    {
                        stageTimer.Stop();
                    }

                    _logger.Info($"{indent}{FormatStageTag(definition.Name, stage.Name)} completed in {stageTimer.Elapsed.TotalMilliseconds:F0} ms.");
                    ApplyStageSetters(stage, context);
                    index++;
                }
                else
                {
                    using var stageActivity = Telemetry.ActivitySource.StartActivity(TelemetryConstants.ActivityWorkflowStage);
                    stageActivity?.SetTag(TelemetryConstants.TagStageName, stage.Name);
                    stageActivity?.SetTag(TelemetryConstants.TagStageKind, stage.Kind.ToString());
                    if (verbose)
                    {
                        _logger.Info($"{indent}{FormatStageTag(definition.Name, stage.Name)} started [Endpoint].");
                    }

                    var stageTimer = Stopwatch.StartNew();
                    string? jumpTarget;
                    try
                    {
                        jumpTarget = await ExecuteEndpointStageAsync(definition, stage, apiBaseUrls, context, verbose, document.FilePath, mocked, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        stageActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                        _logger.Error($"{indent}{FormatStageTag(definition.Name, stage.Name)} failed after {stageTimer.Elapsed.TotalMilliseconds:F0} ms: {ex.Message}");
                        if (captureErrors)
                        {
                            return BuildWorkflowErrorResult(definition, context, ex);
                        }

                        throw;
                    }
                    finally
                    {
                        stageTimer.Stop();
                    }

                    _logger.Info($"{indent}{FormatStageTag(definition.Name, stage.Name)} completed in {stageTimer.Elapsed.TotalMilliseconds:F0} ms.");
                    ApplyStageSetters(stage, context);
                    if (!string.IsNullOrWhiteSpace(jumpTarget))
                    {
                        if (verbose)
                        {
                            _logger.Info($"{indent}{FormatStageTag(definition.Name, stage.Name)} jump target: {jumpTarget}.");
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
        }

        var workflowOutput = ResolveWorkflowOutput(definition, context);
        context.WorkflowOutputs[definition.Name] = workflowOutput;

        ApplyEndStageContext(definition, context);
        _logger.Info($"{indent}{FormatWorkflowTag(definition.Name)}#endStage processed.");

        if (definition.Output)
        {
            context.OutputFilePath = await _outputWriter.WriteOutputAsync(
                definition,
                document,
                workflowOutput,
                cancellationToken);
        }

        workflowTimer.Stop();
        _logger.Info($"{indent}Workflow {FormatWorkflowTag(definition.Name)} completed in {workflowTimer.Elapsed.TotalMilliseconds:F0} ms.");
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

    private static Dictionary<string, string> BuildApiBaseUrlLookup(
        WorkflowDefinition definition,
        ApiCatalogVersion catalogVersion,
        string environment)
    {
        var apiBaseUrls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (definition.References?.Apis is null || definition.References.Apis.Count == 0)
        {
            return apiBaseUrls;
        }

        foreach (var apiReference in definition.References.Apis)
        {
            var apiDefinition = catalogVersion.Definitions.FirstOrDefault(def =>
                string.Equals(def.Name, apiReference.Definition, StringComparison.OrdinalIgnoreCase));
            if (apiDefinition is null)
            {
                throw new InvalidOperationException(
                    $"API definition '{apiReference.Definition}' was not found in catalog version '{catalogVersion.Version}'.");
            }

            if (!ApiBaseUrlResolver.TryResolveBaseUrl(catalogVersion, apiDefinition, environment, out var baseUrl))
            {
                throw new InvalidOperationException(
                    $"Environment '{environment}' was not found for API definition '{apiDefinition.Name}' in catalog version '{catalogVersion.Version}'.");
            }

            apiBaseUrls[apiReference.Name] = CombineBaseUrl(baseUrl!, apiDefinition.BasePath);
        }

        return apiBaseUrls;
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
            _logger.Info($"{GetIndent(context)}{FormatStageTag(workflowName, stage.Name)} delay: {delaySeconds}s.");
        }

        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
    }

    private static string CombineBaseUrl(string baseUrl, string? basePath)
    {
        if (string.IsNullOrWhiteSpace(basePath))
        {
            return baseUrl;
        }

        var trimmedBaseUrl = baseUrl.TrimEnd('/');
        var trimmedBasePath = basePath.Trim('/');
        return $"{trimmedBaseUrl}/{trimmedBasePath}";
    }

    private async Task ExecuteWorkflowStageAsync(
        WorkflowDocument document,
        WorkflowStageDefinition stage,
        ApiCatalogVersion catalogVersion,
        string environment,
        ExecutionContext context,
        bool varsOverrideActive,
        bool mocked,
        bool verbose,
        bool debug,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stage.WorkflowRef))
        {
            throw new InvalidOperationException($"Stage '{stage.Name}' workflowRef is required.");
        }

        var reference = document.Definition.References?.Workflows?.FirstOrDefault(item =>
            string.Equals(item.Name, stage.WorkflowRef, StringComparison.OrdinalIgnoreCase));
        if (reference is null)
        {
            throw new InvalidOperationException($"Workflow reference '{stage.WorkflowRef}' was not found.");
        }

        var nestedPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(document.FilePath) ?? string.Empty, reference.Path));
        var nestedDocument = _workflowLoader.Load(nestedPath, context.EnvironmentVariables);
        if (verbose)
        {
            _logger.Info($"{GetIndent(context)}{FormatStageTag(document.Definition.Name, stage.Name)} resolved workflow '{nestedDocument.Definition.Name}' at '{nestedDocument.FilePath}'.");
        }

        _logger.Info($"{GetIndent(context)}Calling nested workflow {FormatWorkflowTag(nestedDocument.Definition.Name)} from stage {FormatStageTag(document.Definition.Name, stage.Name)}.");
        if (verbose)
        {
            _logger.Info($"{GetIndent(context.IndentLevel + 1)}Workflow loaded: {nestedDocument.Definition.Name} ({nestedDocument.Definition.Id}).");
        }

        var nestedInputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var hasStageInputs = stage.Inputs is not null && stage.Inputs.Count > 0;
        if (hasStageInputs)
        {
            foreach (var pair in stage.Inputs)
            {
                var resolved = _templateResolver.ResolveTemplate(pair.Value, context.BuildTemplateContext());
                nestedInputs[pair.Key] = resolved;
            }
        }

        var nestedVarsPath = ResolveAutoVarsFilePath(nestedDocument.FilePath);
        if (nestedVarsPath is not null)
        {
            if (varsOverrideActive)
            {
                _logger.Info($"{GetIndent(context.IndentLevel + 1)}Vars file: overrided by main workflow");
            }
            else if (!hasStageInputs)
            {
                try
                {
                    var varsResolution = _varsFileLoader.LoadWithDetails(nestedVarsPath, environment, nestedDocument.Definition.Version);
                    nestedInputs.Clear();
                    foreach (var pair in varsResolution.Values)
                    {
                        nestedInputs[pair.Key] = pair.Value;
                    }

                    _logger.Info($"{GetIndent(context.IndentLevel + 1)}Vars file: {nestedVarsPath} (auto)");
                    if (verbose)
                    {
                        LogVarsSources(varsResolution, context.IndentLevel + 2);
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
            context.Context,
            context.IndentLevel + 1);
        var nestedResult = await ExecuteWorkflowAsync(
            nestedDocument,
            catalogVersion,
            environment,
            nestedContext,
            varsOverrideActive,
            mocked,
            verbose,
            debug,
            cancellationToken,
            captureErrors: true);
        if (nestedContext.WorkflowOutputs.TryGetValue(nestedDocument.Definition.Name, out var nestedOutput))
        {
            context.WorkflowOutputs[stage.Name] = nestedOutput;
        }
        else
        {
            context.WorkflowOutputs[stage.Name] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        ApplyWorkflowStageResult(context, stage.Name, nestedResult.Status, nestedResult.Message);
        _logger.Info(string.Empty);
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

    private async Task<string?> ExecuteEndpointStageAsync(
        WorkflowDefinition definition,
        WorkflowStageDefinition stage,
        IReadOnlyDictionary<string, string> apiBaseUrls,
        ExecutionContext context,
        bool verbose,
        string workflowPath,
        bool mocked,
        CancellationToken cancellationToken)
    {
        var retryPolicy = ResolveRetryPolicy(definition, stage);
        var circuitPolicy = ResolveCircuitBreakerPolicy(definition, stage, retryPolicy);
        if (circuitPolicy is not null && IsCircuitOpen(circuitPolicy, context))
        {
            EmitCircuitBreakerMessage(circuitPolicy.OnBlockedMessage, context);
            throw new InvalidOperationException($"Circuit breaker '{circuitPolicy.Name}' is open for stage '{stage.Name}'.");
        }

        ResponseContext responseContext;
        var retriesUsed = 0;
        while (true)
        {
            if (mocked && stage.Mock is not null)
            {
                responseContext = BuildMockResponse(stage, context, workflowPath);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(stage.ApiRef) || !apiBaseUrls.TryGetValue(stage.ApiRef, out var baseUrl))
                {
                    throw new InvalidOperationException($"Stage '{stage.Name}' apiRef '{stage.ApiRef}' was not found in workflow references.");
                }

                EndpointInvocationResult invocation;
                try
                {
                    invocation = await _endpointInvoker.InvokeAsync(
                        stage,
                        baseUrl,
                        context.BuildTemplateContext(),
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    if (retryPolicy is not null && retriesUsed < retryPolicy.MaxRetries)
                    {
                        retriesUsed++;
                        _logger.Info($"{GetIndent(context)}{FormatStageTag(definition.Name, stage.Name)} retrying in {retryPolicy.DelayMs}ms after exception (retry {retriesUsed}/{retryPolicy.MaxRetries}).");
                        await Task.Delay(retryPolicy.DelayMs, cancellationToken);
                        continue;
                    }

                    EmitRetryExceptionMessage(retryPolicy, context);
                    throw new InvalidOperationException($"Stage '{stage.Name}' failed with exception: {ex.Message}", ex);
                }

                responseContext = invocation.Response;

                if (verbose)
                {
                    _logger.Info($"{GetIndent(context)}{FormatStageTag(definition.Name, stage.Name)} request: {invocation.HttpMethod} {invocation.RequestUri}");
                    _logger.Info($"{GetIndent(context)}{FormatStageTag(definition.Name, stage.Name)} response status: {responseContext.StatusCode}.");
                }

                if (responseContext.StatusCode == (int)System.Net.HttpStatusCode.BadRequest)
                {
                    var responseDump = string.IsNullOrWhiteSpace(responseContext.Body) ? "<empty>" : responseContext.Body;
                    _logger.Error($"{GetIndent(context)}{FormatStageTag(definition.Name, stage.Name)} returned 400. Response body: {responseDump}");
                    if (verbose)
                    {
                        var requestBody = string.IsNullOrWhiteSpace(invocation.RequestBody) ? "<empty>" : invocation.RequestBody;
                        _logger.Error($"{GetIndent(context)}{FormatStageTag(definition.Name, stage.Name)} request body: {requestBody}");
                    }
                }

                if (responseContext.StatusCode == (int)System.Net.HttpStatusCode.NotFound)
                {
                    _logger.Error($"{GetIndent(context)}{FormatStageTag(definition.Name, stage.Name)} returned 404 for url: {invocation.RequestUri}");
                }
            }

            if (retryPolicy is not null &&
                retryPolicy.HttpStatus.Contains(responseContext.StatusCode) &&
                retriesUsed < retryPolicy.MaxRetries)
            {
                retriesUsed++;
                _logger.Info($"{GetIndent(context)}{FormatStageTag(definition.Name, stage.Name)} retrying in {retryPolicy.DelayMs}ms (retry {retriesUsed}/{retryPolicy.MaxRetries}).");
                await Task.Delay(retryPolicy.DelayMs, cancellationToken);
                continue;
            }

            break;
        }

        if (circuitPolicy is not null)
        {
            UpdateCircuitBreaker(circuitPolicy, context, responseContext.StatusCode);
        }

        if (stage.ExpectedStatus.HasValue && responseContext.StatusCode != stage.ExpectedStatus.Value)
        {
            throw new InvalidOperationException(
                $"Stage '{stage.Name}' returned {responseContext.StatusCode} but expected {stage.ExpectedStatus.Value}.");
        }

        var stageOutput = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (stage.Output is not null)
        {
            foreach (var output in stage.Output)
            {
                var resolved = _templateResolver.ResolveTemplate(output.Value, context.BuildTemplateContext(), responseContext);
                stageOutput[output.Key] = resolved;
            }
        }

        if (!stageOutput.ContainsKey("http_status"))
        {
            stageOutput["http_status"] = responseContext.StatusCode.ToString();
        }

        context.EndpointOutputs[stage.Name] = stageOutput;
        PrintStageMessage(definition, stage, context, responseContext);

        if (stage.JumpOnStatus is not null &&
            stage.JumpOnStatus.TryGetValue(responseContext.StatusCode, out var jumpTarget))
        {
            return jumpTarget;
        }

        return null;
    }

    private RetryPolicy? ResolveRetryPolicy(WorkflowDefinition definition, WorkflowStageDefinition stage)
    {
        if (stage.Retry is null)
        {
            return null;
        }

        RetryPolicyDefinition? shared = null;
        if (!string.IsNullOrWhiteSpace(stage.Retry.Ref))
        {
            definition.Resilience?.Retries?.TryGetValue(stage.Retry.Ref, out shared);
        }

        var maxRetries = stage.Retry.MaxRetries ?? shared?.MaxRetries;
        var delayMs = stage.Retry.DelayMs ?? shared?.DelayMs;
        var httpStatus = stage.Retry.HttpStatus ?? Array.Empty<int>();

        if (maxRetries is null || delayMs is null || httpStatus.Length == 0)
        {
            return null;
        }

        var onExceptionMessage = stage.Retry.Messages?.OnException;
        return new RetryPolicy(maxRetries.Value, delayMs.Value, httpStatus, onExceptionMessage);
    }

    private CircuitBreakerPolicy? ResolveCircuitBreakerPolicy(
        WorkflowDefinition definition,
        WorkflowStageDefinition stage,
        RetryPolicy? retryPolicy)
    {
        if (stage.CircuitBreaker is null)
        {
            return null;
        }

        CircuitBreakerDefinition? shared = null;
        if (!string.IsNullOrWhiteSpace(stage.CircuitBreaker.Ref))
        {
            definition.Resilience?.CircuitBreakers?.TryGetValue(stage.CircuitBreaker.Ref, out shared);
        }

        var failureThreshold = stage.CircuitBreaker.FailureThreshold ?? shared?.FailureThreshold;
        var breakMs = stage.CircuitBreaker.BreakMs ?? shared?.BreakMs;
        var closeOnSuccessAttempts = stage.CircuitBreaker.CloseOnSuccessAttempts
            ?? shared?.CloseOnSuccessAttempts
            ?? 1;
        var httpStatus = retryPolicy?.HttpStatus ?? Array.Empty<int>();

        if (failureThreshold is null || breakMs is null || httpStatus.Count == 0)
        {
            return null;
        }

        var name = string.IsNullOrWhiteSpace(stage.CircuitBreaker.Ref)
            ? stage.Name
            : stage.CircuitBreaker.Ref;

        var onOpenMessage = stage.CircuitBreaker.Messages?.OnOpen;
        var onBlockedMessage = stage.CircuitBreaker.Messages?.OnBlocked;
        return new CircuitBreakerPolicy(
            name,
            failureThreshold.Value,
            breakMs.Value,
            httpStatus,
            onOpenMessage,
            onBlockedMessage,
            closeOnSuccessAttempts);
    }

    private bool IsCircuitOpen(CircuitBreakerPolicy policy, ExecutionContext context)
    {
        if (!context.CircuitBreakers.TryGetValue(policy.Name, out var state))
        {
            state = new CircuitBreakerState();
            context.CircuitBreakers[policy.Name] = state;
            return false;
        }

        if (state.OpenUntil is null)
        {
            return false;
        }

        if (_systemProvider.UtcNow >= state.OpenUntil.Value)
        {
            state.OpenUntil = null;
            state.ConsecutiveFailures = 0;
            state.ConsecutiveSuccesses = 0;
            state.HalfOpen = true;
            return false;
        }

        return true;
    }

    private void UpdateCircuitBreaker(CircuitBreakerPolicy policy, ExecutionContext context, int statusCode)
    {
        if (!context.CircuitBreakers.TryGetValue(policy.Name, out var state))
        {
            state = new CircuitBreakerState();
            context.CircuitBreakers[policy.Name] = state;
        }

        if (policy.HttpStatus.Contains(statusCode))
        {
            state.ConsecutiveSuccesses = 0;
            if (state.HalfOpen)
            {
                state.OpenUntil = _systemProvider.UtcNow.AddMilliseconds(policy.BreakMs);
                state.HalfOpen = false;
                state.ConsecutiveFailures = 0;
                EmitCircuitBreakerMessage(policy.OnOpenMessage, context);
                return;
            }

            state.ConsecutiveFailures++;
            if (state.ConsecutiveFailures >= policy.FailureThreshold)
            {
                state.OpenUntil = _systemProvider.UtcNow.AddMilliseconds(policy.BreakMs);
                state.ConsecutiveFailures = 0;
                EmitCircuitBreakerMessage(policy.OnOpenMessage, context);
            }
            return;
        }

        state.ConsecutiveFailures = 0;
        if (state.HalfOpen)
        {
            state.ConsecutiveSuccesses++;
            if (state.ConsecutiveSuccesses >= policy.CloseOnSuccessAttempts)
            {
                state.HalfOpen = false;
                state.ConsecutiveSuccesses = 0;
            }
            return;
        }

        state.ConsecutiveSuccesses = 0;
    }

    private void EmitCircuitBreakerMessage(string? message, ExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var resolved = _templateResolver.ResolveTemplate(message, context.BuildTemplateContext());
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            _logger.Info($"{GetIndent(context)}{resolved}");
        }
    }

    private void EmitRetryExceptionMessage(RetryPolicy? policy, ExecutionContext context)
    {
        if (policy is null || string.IsNullOrWhiteSpace(policy.OnExceptionMessage))
        {
            return;
        }

        var resolved = _templateResolver.ResolveTemplate(policy.OnExceptionMessage, context.BuildTemplateContext());
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            _logger.Error($"{GetIndent(context)}{resolved}");
        }
    }

    private void PrintStageMessage(
        WorkflowDefinition definition,
        WorkflowStageDefinition stage,
        ExecutionContext context,
        ResponseContext? responseContext)
    {
        if (string.IsNullOrWhiteSpace(stage.Message))
        {
            return;
        }

        var resolved = _templateResolver.ResolveTemplate(stage.Message, context.BuildTemplateContext(), responseContext);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            return;
        }

        _logger.Info($"{GetIndent(context)}{FormatStageTag(definition.Name, stage.Name)} message: {resolved}");
    }

    private void PrintStageDebug(WorkflowDefinition definition, WorkflowStageDefinition stage, ExecutionContext context)
    {
        if (stage.Debug is null || stage.Debug.Count == 0)
        {
            return;
        }

        var indent = GetIndent(context);
        _logger.Info($"{indent}{FormatStageTag(definition.Name, stage.Name)} debug:");
        var templateContext = context.BuildTemplateContext();
        foreach (var pair in stage.Debug)
        {
            var resolved = _templateResolver.ResolveTemplate(pair.Value, templateContext);
            _logger.Info($"{indent} {pair.Key}: {resolved}");
        }
    }

    private ResponseContext BuildMockResponse(WorkflowStageDefinition stage, ExecutionContext context, string workflowPath)
    {
        if (!string.IsNullOrWhiteSpace(stage.Mock?.Payload) && !string.IsNullOrWhiteSpace(stage.Mock?.PayloadFile))
        {
            throw new InvalidOperationException($"Stage '{stage.Name}' mock cannot define both payload and payloadFile.");
        }

        var rawPayload = string.IsNullOrWhiteSpace(stage.Mock?.PayloadFile)
            ? _mockPayloadService.LoadRawPayload(stage.Mock?.Payload ?? string.Empty, workflowPath)
            : _mockPayloadService.LoadRawPayloadFromFile(stage.Mock?.PayloadFile ?? string.Empty, workflowPath);

        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            throw new InvalidOperationException($"Stage '{stage.Name}' mock payload is required.");
        }
        var resolvedPayload = _templateResolver.ResolveTemplate(rawPayload, context.BuildTemplateContext());
        JsonDocument json;
        try
        {
            json = JsonDocument.Parse(resolvedPayload);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Stage '{stage.Name}' mock payload is not valid JSON: {ex.Message}");
        }

        var status = stage.Mock?.Status ?? stage.ExpectedStatus ?? 200;
        return new ResponseContext(
            status,
            resolvedPayload,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            json);
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

    private WorkflowResultInfo BuildWorkflowSuccessResult(WorkflowDefinition definition, ExecutionContext context)
    {
        var message = string.Empty;
        var template = definition.EndStage?.Result?.Message;
        if (!string.IsNullOrWhiteSpace(template))
        {
            message = _templateResolver.ResolveTemplate(template, context.BuildTemplateContext());
        }

        return ApplyWorkflowResult(definition.Name, context, WorkflowResultStatus.Ok.ToString(), message);
    }

    private WorkflowResultInfo BuildWorkflowErrorResult(WorkflowDefinition definition, ExecutionContext context, Exception ex)
    {
        return ApplyWorkflowResult(definition.Name, context, WorkflowResultStatus.Error.ToString(), ex.Message);
    }

    private WorkflowResultInfo ApplyWorkflowResult(
        string workflowName,
        ExecutionContext context,
        string status,
        string message)
    {
        var result = new WorkflowResultInfo(status, message);
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

    private sealed class ExecutionContext
    {
        public ExecutionContext(
            IReadOnlyDictionary<string, string> inputs,
            IReadOnlyDictionary<string, string> environmentVariables,
            IDictionary<string, string>? parentContext = null,
            int indentLevel = 0)
        {
            Inputs = new Dictionary<string, string>(inputs, StringComparer.OrdinalIgnoreCase);
            EnvironmentVariables = new Dictionary<string, string>(environmentVariables, StringComparer.OrdinalIgnoreCase);
            Globals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            EndpointOutputs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            WorkflowOutputs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            WorkflowResults = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            CircuitBreakers = new Dictionary<string, CircuitBreakerState>(StringComparer.OrdinalIgnoreCase);
            Context = parentContext is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(parentContext, StringComparer.OrdinalIgnoreCase);
            IndentLevel = Math.Max(0, indentLevel);
        }

        public Dictionary<string, string> Inputs { get; }
        public Dictionary<string, string> EnvironmentVariables { get; }
        public Dictionary<string, string> Globals { get; }
        public Dictionary<string, string> Context { get; }
        public Dictionary<string, IReadOnlyDictionary<string, string>> EndpointOutputs { get; }
        public Dictionary<string, IReadOnlyDictionary<string, string>> WorkflowOutputs { get; }
        public Dictionary<string, IReadOnlyDictionary<string, string>> WorkflowResults { get; }
        public Dictionary<string, CircuitBreakerState> CircuitBreakers { get; }
        public string? OutputFilePath { get; set; }
        public int IndentLevel { get; }

        public TemplateContext BuildTemplateContext()
        {
            return new TemplateContext(Inputs, Globals, Context, EndpointOutputs, WorkflowOutputs, WorkflowResults, EnvironmentVariables);
        }
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

    private bool ShouldRunStage(WorkflowStageDefinition stage, ExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(stage.RunIf))
        {
            return true;
        }

        return EvaluateCondition(stage.RunIf, context);
    }

    private bool EvaluateCondition(string expression, ExecutionContext context)
    {
        if (!RunIfParser.TryParse(expression, out var token, out var op, out var rawValue))
        {
            throw new InvalidOperationException($"Invalid runIf expression '{expression}'.");
        }

        var actual = ResolveNullableToken(token, context);
        if (op.Equals("in", StringComparison.OrdinalIgnoreCase))
        {
            var values = NormalizeRunIfList(rawValue);
            return values.Contains(actual ?? string.Empty);
        }

        if (op.Equals("not in", StringComparison.OrdinalIgnoreCase))
        {
            var values = NormalizeRunIfList(rawValue);
            return !values.Contains(actual ?? string.Empty);
        }

        var expected = NormalizeRunIfValue(rawValue, out var expectedIsNull);
        var isEqual = expectedIsNull
            ? string.IsNullOrEmpty(actual)
            : string.Equals(actual ?? string.Empty, expected ?? string.Empty, StringComparison.Ordinal);
        return op == "==" ? isEqual : !isEqual;
    }

    private static string? NormalizeRunIfValue(string rawValue, out bool expectedIsNull)
    {
        expectedIsNull = rawValue.Equals("null", StringComparison.OrdinalIgnoreCase);
        if (expectedIsNull)
        {
            return null;
        }

        if (rawValue.Length >= 2 &&
            ((rawValue.StartsWith('"') && rawValue.EndsWith('"')) ||
             (rawValue.StartsWith('\'') && rawValue.EndsWith('\''))))
        {
            return rawValue[1..^1];
        }

        return rawValue;
    }

    private static HashSet<string> NormalizeRunIfList(string rawValue)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);
        if (!rawValue.StartsWith('[') || !rawValue.EndsWith(']'))
        {
            return values;
        }

        var inner = rawValue[1..^1];
        if (string.IsNullOrWhiteSpace(inner))
        {
            return values;
        }

        foreach (var item in inner.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            values.Add(item);
        }

        return values;
    }

    private string? ResolveNullableToken(string token, ExecutionContext context)
    {
        var segments = TemplateResolver.SplitToken(token);
        if (segments.Length < 2)
        {
            return null;
        }

        var root = segments[0].ToLowerInvariant();
        var name = segments[1];

        return root switch
        {
            "input" => context.Inputs.TryGetValue(name, out var inputValue) ? inputValue : null,
            "global" => context.Globals.TryGetValue(name, out var globalValue) ? globalValue : null,
            "context" => context.Context.TryGetValue(name, out var contextValue) ? contextValue : null,
            "endpoint" => TryResolveStageOutput(context.EndpointOutputs, name, segments, out var endpointValue) ? endpointValue : null,
            "workflow" => TryResolveStageOutput(context.WorkflowOutputs, name, segments, out var workflowValue) ? workflowValue : null,
            "stage" => TryResolveStageOutput(context.EndpointOutputs, name, segments, out var stageEndpointValue)
                ? stageEndpointValue
                : TryResolveStageOutput(context.WorkflowOutputs, name, segments, out var stageWorkflowValue)
                    ? stageWorkflowValue
                    : TryResolveStageWorkflowResult(context.WorkflowResults, name, segments, out var stageWorkflowResultValue)
                        ? stageWorkflowResultValue
                        : null,
            "env" => context.EnvironmentVariables.TryGetValue(name, out var envValue)
                ? envValue
                : Environment.GetEnvironmentVariable(name),
            "system" => ResolveSystemToken(segments),
            _ => null
        };
    }

    private string? ResolveSystemToken(string[] segments)
    {
        if (segments.Length < 3)
        {
            return null;
        }

        if (segments[1].Equals("datetime", StringComparison.OrdinalIgnoreCase))
        {
            if (segments[2].Equals("now", StringComparison.OrdinalIgnoreCase))
            {
                return _systemProvider.Now.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            }

            if (segments[2].Equals("utcnow", StringComparison.OrdinalIgnoreCase))
            {
                return _systemProvider.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            }

            return null;
        }

        if (segments[1].Equals("date", StringComparison.OrdinalIgnoreCase))
        {
            if (segments[2].Equals("now", StringComparison.OrdinalIgnoreCase))
            {
                return DateOnly.FromDateTime(_systemProvider.Now.DateTime)
                    .ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            }

            if (segments[2].Equals("utcnow", StringComparison.OrdinalIgnoreCase))
            {
                return DateOnly.FromDateTime(_systemProvider.UtcNow.DateTime)
                    .ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            }

            return null;
        }

        if (segments[1].Equals("time", StringComparison.OrdinalIgnoreCase))
        {
            if (segments[2].Equals("now", StringComparison.OrdinalIgnoreCase))
            {
                return TimeOnly.FromDateTime(_systemProvider.Now.DateTime)
                    .ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            }

            if (segments[2].Equals("utcnow", StringComparison.OrdinalIgnoreCase))
            {
                return TimeOnly.FromDateTime(_systemProvider.UtcNow.DateTime)
                    .ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            }

            return null;
        }

        return null;
    }

    private static bool TryResolveStageOutput(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> outputs,
        string stageName,
        string[] segments,
        out string value)
    {
        value = string.Empty;
        if (segments.Length < 4 || !segments[2].Equals("output", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!outputs.TryGetValue(stageName, out var stageOutputs))
        {
            return false;
        }

        var key = segments[3];
        if (!stageOutputs.TryGetValue(key, out var found) || found is null)
        {
            value = string.Empty;
            return false;
        }

        value = found;
        return true;
    }

    private static bool TryResolveStageWorkflowResult(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> results,
        string stageName,
        string[] segments,
        out string value)
    {
        value = string.Empty;
        if (segments.Length < 5 ||
            !segments[2].Equals("workflow", StringComparison.OrdinalIgnoreCase) ||
            !segments[3].Equals("result", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!results.TryGetValue(stageName, out var stageResults))
        {
            return false;
        }

        var key = segments[4];
        if (!stageResults.TryGetValue(key, out var found) || found is null)
        {
            value = string.Empty;
            return false;
        }

        value = found;
        return true;
    }

    private void LogVarsSources(VarsFileResolution resolution, int indentLevel)
    {
        var indent = GetIndent(indentLevel);
        if (resolution.Sources.Count == 0)
        {
            _logger.Info($"{indent}Vars file variable sources: (none)");
            return;
        }

        _logger.Info($"{indent}Vars file variable sources:");
        foreach (var pair in resolution.Sources.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            _logger.Info($"{indent}  {pair.Key}: {FormatVarsSource(pair.Value)}");
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

    private void ApplyWorkflowMock(WorkflowStageDefinition stage, ExecutionContext context)
    {
        if (stage.Mock?.Output is null || stage.Mock.Output.Count == 0)
        {
            throw new InvalidOperationException($"Stage '{stage.Name}' mock output is required for workflow stages.");
        }

        var output = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in stage.Mock.Output)
        {
            var resolved = _templateResolver.ResolveTemplate(pair.Value, context.BuildTemplateContext());
            output[pair.Key] = resolved;
        }

        context.WorkflowOutputs[stage.Name] = output;
    }

}

public sealed record WorkflowExecutionResult(
    IReadOnlyDictionary<string, string> Output,
    string? OutputFilePath);
