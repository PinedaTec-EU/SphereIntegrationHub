using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Net.Http.Headers;
using System.Text;

using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Plugins;
using SphereIntegrationHub.Services.Interfaces;

namespace SphereIntegrationHub.Services;

public sealed class WorkflowExecutor
{
    private readonly HttpClient _httpClient;
    private readonly DynamicValueService _dynamicValueService;
    private readonly TemplateResolver _templateResolver;
    private readonly RandomValueFormattingOptions _formatting;
    private readonly MockPayloadService _mockPayloadService;
    private readonly WorkflowDataFileService _dataFileService;
    private readonly WorkflowLoader _workflowLoader;
    private readonly VarsFileLoader _varsFileLoader;
    private readonly ISystemTimeProvider _systemProvider;
    private readonly IEndpointInvoker _endpointInvoker;
    private readonly IWorkflowOutputWriter _outputWriter;
    private readonly IWorkflowExecutionReportWriter _reportWriter;
    private readonly IExecutionLogger _logger;
    private readonly WorkflowExpressionEvaluator _expressionEvaluator;
    private readonly WorkflowExecutionReportOptions _reportOptions;
    private readonly StagePluginRegistry _stagePluginRegistry;
    private readonly IReadOnlyCollection<string> _preloadedSecretValues;

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
        IRequestBodyContractProcessor? requestBodyContractProcessor = null,
        IWorkflowOutputWriter? outputWriter = null,
        IExecutionLogger? logger = null,
        IWorkflowExecutionReportWriter? reportWriter = null,
        WorkflowExecutionReportOptions? reportOptions = null,
        StagePluginRegistry? stagePluginRegistry = null,
        IReadOnlyCollection<string>? preloadedSecretValues = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _dynamicValueService = dynamicValueService ?? throw new ArgumentNullException(nameof(dynamicValueService));
        _workflowLoader = workflowLoader ?? new WorkflowLoader();
        _varsFileLoader = varsFileLoader ?? new VarsFileLoader();
        _systemProvider = systemProvider ?? new SystemTimeProvider();
        _templateResolver = templateResolver ?? new TemplateResolver(_systemProvider);
        _mockPayloadService = mockPayloadService ?? new MockPayloadService();
        _dataFileService = new WorkflowDataFileService();
        _formatting = formatting ?? RandomValueFormattingOptions.Default;
        _endpointInvoker = endpointInvoker ?? new HttpEndpointInvoker(_httpClient, _templateResolver, requestBodyContractProcessor);
        _outputWriter = outputWriter ?? new WorkflowOutputWriter();
        _reportWriter = reportWriter ?? new WorkflowExecutionReportWriter();
        _logger = logger ?? new ConsoleExecutionLogger();
        _expressionEvaluator = new WorkflowExpressionEvaluator(_templateResolver);
        _reportOptions = reportOptions ?? WorkflowExecutionReportOptions.Default;
        _stagePluginRegistry = stagePluginRegistry ?? new StagePluginRegistryBuilder().CreateBuiltInRegistry();
        _preloadedSecretValues = preloadedSecretValues ?? Array.Empty<string>();
    }

    private static string FormatWorkflowTag(string name) => $"[{name}]";

    private static string FormatStageTag(string workflowName, string stageName)
        => $"{FormatWorkflowTag(workflowName)}#{stageName}";

    private static string FormatWorkflowHeader(string name, string version) => $"workflow {FormatWorkflowTag(name)} (version {version})";

    private static string FormatStepEntry(string stageName) => $"- step: {stageName}";

    private static string FormatWorkflowCompletion(double elapsedMilliseconds)
        => $"- completed in {elapsedMilliseconds:F0} ms.";

    private static string GetStepIndent(ExecutionContext context) => GetIndent(context.IndentLevel + 2);

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

    private sealed class IterationScope : IDisposable
    {
        private readonly ExecutionContext _context;
        private readonly string _itemName;
        private readonly string _indexName;
        private readonly string? _previousItem;
        private readonly JsonElement? _previousItemJson;
        private readonly string? _previousIndex;
        private readonly JsonElement? _previousIndexJson;
        private readonly bool _hadItem;
        private readonly bool _hadIndex;

        public IterationScope(ExecutionContext context, string itemName, string indexName, JsonElement item, int index)
        {
            _context = context;
            _itemName = itemName;
            _indexName = indexName;
            _hadItem = context.Context.TryGetValue(itemName, out _previousItem);
            _hadIndex = context.Context.TryGetValue(indexName, out _previousIndex);
            if (context.ContextJson.TryGetValue(itemName, out var previousItemJson))
            {
                _previousItemJson = previousItemJson.Clone();
            }

            if (context.ContextJson.TryGetValue(indexName, out var previousIndexJson))
            {
                _previousIndexJson = previousIndexJson.Clone();
            }

            context.Context[itemName] = JsonValueHelper.ToDisplayString(item);
            context.ContextJson[itemName] = item.Clone();
            context.Context[indexName] = index.ToString();
            context.ContextJson[indexName] = JsonSerializer.SerializeToElement(index);
        }

        public void Dispose()
        {
            if (_hadItem)
            {
                _context.Context[_itemName] = _previousItem ?? string.Empty;
            }
            else
            {
                _context.Context.Remove(_itemName);
            }

            if (_previousItemJson.HasValue)
            {
                _context.ContextJson[_itemName] = _previousItemJson.Value.Clone();
            }
            else
            {
                _context.ContextJson.Remove(_itemName);
            }

            if (_hadIndex)
            {
                _context.Context[_indexName] = _previousIndex ?? string.Empty;
            }
            else
            {
                _context.Context.Remove(_indexName);
            }

            if (_previousIndexJson.HasValue)
            {
                _context.ContextJson[_indexName] = _previousIndexJson.Value.Clone();
            }
            else
            {
                _context.ContextJson.Remove(_indexName);
            }
        }
    }

    private sealed record WorkflowResultInfo(string Status, string Message);
    private sealed record WorkflowForEachIterationResult(
        int Index,
        IReadOnlyDictionary<string, string> Output,
        JsonElement OutputJson,
        IReadOnlyDictionary<string, string> WorkflowResult,
        JsonElement WorkflowResultJson,
        IReadOnlyCollection<string> SecretValues);
    private sealed record EndpointForEachIterationResult(
        int Index,
        IReadOnlyDictionary<string, string> Output,
        JsonElement OutputJson,
        string? JumpTarget,
        IReadOnlyCollection<string> SecretValues);

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
        => await ExecuteAsync(document, catalogVersion, environment, inputs, varsOverrideActive, mocked, verbose, debug, null, cancellationToken);

    public async Task<WorkflowExecutionResult> ExecuteAsync(
        WorkflowDocument document,
        ApiCatalogVersion catalogVersion,
        string environment,
        IReadOnlyDictionary<string, string> inputs,
        bool varsOverrideActive,
        bool mocked,
        bool verbose,
        bool debug,
        WorkflowPreflightReport? preflightReport,
        CancellationToken cancellationToken)
    {
        var context = new ExecutionContext(inputs, document.EnvironmentVariables);
        foreach (var secretValue in _preloadedSecretValues.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            context.SecretValues.Add(secretValue);
        }

        ApplyTypedInputs(document.Definition, context);
        context.Report = BuildExecutionReport(document, environment, inputs, mocked, preflightReport);

        try
        {
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

            var successResult = await FinalizeExecutionResultAsync(document, context, cancellationToken, success: true, errorMessage: null);
            return successResult;
        }
        catch (Exception ex)
        {
            var failedResult = await FinalizeExecutionResultAsync(document, context, cancellationToken, success: false, errorMessage: ex.Message);
            ex.Data["workflowExecutionResult"] = failedResult;
            throw;
        }
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
            context.WorkflowVars = definition.Vars;
            _logger.Info($"{indent}{FormatWorkflowHeader(definition.Name, definition.Version)}");

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
                        RecordSkippedStage(definition.Name, stage, context);
                        index++;
                        continue;
                    }

                    await ApplyStageDelayAsync(definition.Name, stage, context, cancellationToken);

                    if (debug)
                    {
                        PrintStageDebug(definition, stage, context);
                    }

                    if (WorkflowStageKind.IsWorkflow(stage.Kind))
                    {
                        var stageRecord = BeginStageRecord(definition.Name, stage, context, isMocked: mocked && stage.Mock is not null);
                        using var stageActivity = Telemetry.ActivitySource.StartActivity(TelemetryConstants.ActivityWorkflowStage);
                        stageActivity?.SetTag(TelemetryConstants.TagStageName, stage.Name);
                        stageActivity?.SetTag(TelemetryConstants.TagStageKind, stage.Kind);
                        if (verbose)
                        {
                            _logger.Info($"{indent}{FormatStageTag(definition.Name, stage.Name)} started [Workflow].");
                        }

                        var stageTimer = Stopwatch.StartNew();
                        try
                        {
                            await ExecuteWorkflowStageWithOptionalForEachAsync(
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
                            PrintStageMessage(definition, stage, context, null);
                            CompleteStageRecord(stageRecord, context, "Ok", null, null);
                        }
                        catch (Exception ex)
                        {
                            stageActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                            _logger.Error($"{GetStepIndent(context)}{FormatStepEntry(stage.Name)} failed after {stageTimer.Elapsed.TotalMilliseconds:F0} ms: {ex.Message}");
                            ApplyWorkflowStageResult(context, stage.Name, WorkflowResultStatus.Error, ex.Message);
                            CompleteStageRecord(stageRecord, context, "Error", null, ex.Message);
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

                        _logger.Info($"{GetStepIndent(context)}{FormatStepEntry(stage.Name)} completed in {stageTimer.Elapsed.TotalMilliseconds:F0} ms.");
                        ApplyStageSetters(stage, context);
                        index++;
                    }
                    else
                    {
                        var stageRecord = BeginStageRecord(definition.Name, stage, context, isMocked: mocked && stage.Mock is not null);
                        using var stageActivity = Telemetry.ActivitySource.StartActivity(TelemetryConstants.ActivityWorkflowStage);
                        stageActivity?.SetTag(TelemetryConstants.TagStageName, stage.Name);
                        stageActivity?.SetTag(TelemetryConstants.TagStageKind, stage.Kind);
                        if (verbose)
                        {
                            _logger.Info($"{indent}{FormatStageTag(definition.Name, stage.Name)} started [{stage.Kind}].");
                        }

                        var stageTimer = Stopwatch.StartNew();
                        string? jumpTarget;
                        try
                        {
                            jumpTarget = await ExecuteEndpointStageWithOptionalForEachAsync(
                                definition,
                                stage,
                                apiBaseUrls,
                                context,
                                verbose,
                                document.FilePath,
                                mocked,
                                cancellationToken);
                            stageRecord.JumpTarget = jumpTarget;
                            CompleteStageRecord(stageRecord, context, string.IsNullOrWhiteSpace(jumpTarget) ? "Ok" : "Jumped", jumpTarget, null);
                        }
                        catch (Exception ex)
                        {
                            stageActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                            _logger.Error($"{GetStepIndent(context)}{FormatStepEntry(stage.Name)} failed after {stageTimer.Elapsed.TotalMilliseconds:F0} ms: {ex.Message}");
                            CompleteStageRecord(stageRecord, context, "Error", null, ex.Message);
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

                        _logger.Info($"{GetStepIndent(context)}{FormatStepEntry(stage.Name)} completed in {stageTimer.Elapsed.TotalMilliseconds:F0} ms.");
                        ApplyStageSetters(stage, context);
                        if (!string.IsNullOrWhiteSpace(jumpTarget))
                        {
                            if (verbose)
                            {
                                _logger.Info($"{indent}{FormatStageTag(definition.Name, stage.Name)} jump target: {jumpTarget}.");
                            }

                            if (string.Equals(jumpTarget, WorkflowConstants.EndStage, StringComparison.OrdinalIgnoreCase))
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
            context.WorkflowOutputsJson[definition.Name] = BuildJsonMap(workflowOutput);

            ApplyEndStageContext(definition, context);
            var endStageSecretKeys = definition.EndStage?.SecretOutputs is { Count: > 0 }
                ? new HashSet<string>(definition.EndStage.SecretOutputs, StringComparer.OrdinalIgnoreCase)
                : null;
            if (definition.Output)
            {
                context.OutputFilePath = await _outputWriter.WriteOutputAsync(
                    definition,
                    document,
                    context.Report?.ExecutionId ?? Ulid.NewUlid().ToString(),
                    workflowOutput,
                    endStageSecretKeys,
                    context.SecretValues,
                    cancellationToken);
            }

            workflowTimer.Stop();
            _logger.Info($"{GetStepIndent(context)}{FormatWorkflowCompletion(workflowTimer.Elapsed.TotalMilliseconds)}");
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
        CancellationToken cancellationToken)
    {
        var delaySeconds = stage.DelaySeconds ?? 0;
        if (delaySeconds <= 0)
        {
            return;
        }

        _logger.Info($"{GetIndent(context)}{FormatStageTag(workflowName, stage.Name)} delay: {delaySeconds}s.");

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

    private async Task<StageTransportResponse> SendTransportRequestAsync(
        StageTransportRequest request,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(new HttpMethod(request.Method), request.RequestUri);
        if (request.Headers is not null)
        {
            foreach (var header in request.Headers)
            {
                message.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Body))
        {
            var content = new StringContent(request.Body, Encoding.UTF8);
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(request.ContentType ?? "application/json");
            message.Content = content;
        }
        else if (!string.IsNullOrWhiteSpace(request.ContentType))
        {
            message.Content = new StringContent(string.Empty, Encoding.UTF8);
            message.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(request.ContentType);
        }

        using var response = await _httpClient.SendAsync(message, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers)
        {
            headers[header.Key] = string.Join(",", header.Value);
        }

        foreach (var header in response.Content.Headers)
        {
            headers[header.Key] = string.Join(",", header.Value);
        }

        var requestBody = message.Content is null
            ? null
            : await message.Content.ReadAsStringAsync(cancellationToken);

        return new StageTransportResponse(
            (int)response.StatusCode,
            body,
            headers,
            message.RequestUri?.ToString() ?? request.RequestUri,
            request.Method,
            requestBody);
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

        string nestedPath;
        WorkflowDocument nestedDocument;
        try
        {
            nestedPath = WorkflowReferencePathResolver.ResolvePath(
                reference.Path,
                document.FilePath,
                context.EnvironmentVariables,
                context.Inputs,
                context.Globals,
                context.Context);
            nestedDocument = _workflowLoader.Load(nestedPath, context.EnvironmentVariables);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Stage '{stage.Name}' workflowRef '{stage.WorkflowRef}' could not load path '{reference.Path}': {ex.Message}",
                ex);
        }
        if (verbose)
        {
            _logger.Info($"{GetIndent(context)}{FormatStageTag(document.Definition.Name, stage.Name)} resolved workflow '{nestedDocument.Definition.Name}' at '{nestedDocument.FilePath}'.");
        }

        if (verbose)
        {
            _logger.Info($"{GetIndent(context.IndentLevel + 1)}Workflow loaded: {nestedDocument.Definition.Name} ({nestedDocument.Definition.Id}) version {nestedDocument.Definition.Version}.");
        }

        var nestedInputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var stageInputs = stage.Inputs;
        var hasStageInputs = stageInputs is { Count: > 0 };
        if (stageInputs is { Count: > 0 })
        {
            var templateContext = context.BuildTemplateContext();
            foreach (var pair in stageInputs)
            {
                var resolved = WorkflowStageInputValueHelper.ResolveToInputString(pair.Value, _templateResolver, templateContext);
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
        nestedContext.Report = context.Report;
        CopyJsonContext(context, nestedContext);
        ApplyTypedInputs(nestedDocument.Definition, nestedContext);
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
            context.WorkflowOutputsJson[stage.Name] = nestedContext.WorkflowOutputsJson.TryGetValue(nestedDocument.Definition.Name, out var nestedOutputJson)
                ? nestedOutputJson
                : BuildJsonMap(nestedOutput);
        }
        else
        {
            context.WorkflowOutputs[stage.Name] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            context.WorkflowOutputsJson[stage.Name] = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        }

        ApplyWorkflowStageResult(context, stage.Name, nestedResult.Status, nestedResult.Message);
        UpdateActiveWorkflowStageRecord(context, nestedInputs, context.WorkflowOutputs[stage.Name], context.WorkflowResults[stage.Name]);
        if (string.Equals(nestedResult.Status, WorkflowResultStatus.Error.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Nested workflow '{nestedDocument.Definition.Name}' failed in stage '{stage.Name}': {nestedResult.Message}");
        }

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
                UpdateActiveStageRecordForResponse(context, stage, responseContext, null, null, retriesUsed, workflowPath, mocked: true);
            }
            else
            {
                if (!_stagePluginRegistry.TryGetByKind(stage.Kind, out var plugin))
                {
                    throw new InvalidOperationException($"Stage '{stage.Name}' kind '{stage.Kind}' is not registered by any active plugin.");
                }

                StagePluginExecutionResult invocation;
                try
                {
                    invocation = await plugin.ExecuteAsync(
                        stage,
                        new StagePluginExecutionContext(
                            template => _templateResolver.ResolveTemplate(template, context.BuildTemplateContext(workflowPath)),
                            path => _dataFileService.LoadText(path, context.BuildTemplateContext(workflowPath)),
                            SendTransportRequestAsync,
                            async (effectiveStage, baseUrl, ct) =>
                            {
                                var endpointInvocation = await _endpointInvoker.InvokeAsync(
                                    effectiveStage,
                                    baseUrl,
                                    context.BuildTemplateContext(workflowPath),
                                    ct);

                                return new StagePluginExecutionResult(
                                    endpointInvocation.Response,
                                    endpointInvocation.RequestUri,
                                    endpointInvocation.HttpMethod,
                                    endpointInvocation.RequestBody);
                            },
                            apiBaseUrls,
                            workflowPath),
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
                UpdateActiveStageRecordForResponse(context, stage, responseContext, invocation.RequestUri, invocation.Operation, retriesUsed, workflowPath, mocked: false, invocation.RequestBody);

                if (verbose)
                {
                    _logger.Info($"{GetIndent(context)}{FormatStageTag(definition.Name, stage.Name)} request: {invocation.Operation} {invocation.RequestUri}");
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
                if (context.ActiveStageRecord is not null)
                {
                    context.ActiveStageRecord.RetryCount = retriesUsed;
                }
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

        Activity.Current?.SetTag(TelemetryConstants.TagHttpStatusCode, responseContext.StatusCode);
        Activity.Current?.SetTag(TelemetryConstants.TagHttpExpectedStatuses, string.Join(",", BuildAllowedStatuses(stage).Order()));

        var stageOutput = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var stageOutputJson = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (stage.Output is not null)
        {
            foreach (var output in stage.Output)
            {
                var resolved = _templateResolver.ResolveTemplate(output.Value, context.BuildTemplateContext(), responseContext);
                stageOutput[output.Key] = resolved;
                if (JsonValueHelper.TryParse(resolved, out var outputJson))
                {
                    stageOutputJson[output.Key] = outputJson;
                }
            }
        }

        if (!stageOutput.ContainsKey("http_status"))
        {
            stageOutput["http_status"] = responseContext.StatusCode.ToString();
        }

        ApplyEnsureOutputs(stage, responseContext.StatusCode, stageOutput, stageOutputJson);

        if (responseContext.Json is not null && !stageOutputJson.ContainsKey("body"))
        {
            stageOutputJson["body"] = responseContext.Json.RootElement.Clone();
        }

        context.EndpointOutputs[stage.Name] = stageOutput;
        context.EndpointOutputsJson[stage.Name] = stageOutputJson;
        var secretOutputKeys = stage.SecretOutputs is { Count: > 0 }
            ? new HashSet<string>(stage.SecretOutputs, StringComparer.OrdinalIgnoreCase)
            : null;
        UpdateActiveStageRecordOutput(context, stageOutput, responseContext.StatusCode, secretOutputKeys);
        PrintStageMessage(definition, stage, context, responseContext);

        if (TryResolveStatusAction(stage, responseContext.StatusCode, out var statusAction))
        {
            ApplyStatusActionOutput(statusAction, stage, context, responseContext, stageOutput, stageOutputJson);
            context.EndpointOutputs[stage.Name] = stageOutput;
            context.EndpointOutputsJson[stage.Name] = stageOutputJson;

            if (statusAction.Fail)
            {
                throw BuildUnexpectedStatusException(stage, responseContext.StatusCode);
            }

            if (!string.IsNullOrWhiteSpace(statusAction.JumpTo))
            {
                return statusAction.JumpTo;
            }
        }

        if (!IsExpectedStatus(stage, responseContext.StatusCode))
        {
            throw BuildUnexpectedStatusException(stage, responseContext.StatusCode);
        }

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

    private async Task ExecuteWorkflowStageWithOptionalForEachAsync(
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
        if (!HasForEach(stage))
        {
            if (mocked && stage.Mock is not null)
            {
                ApplyWorkflowMock(stage, context);
                ApplyWorkflowStageResult(context, stage.Name, WorkflowResultStatus.Ok, string.Empty);
                return;
            }

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
            return;
        }

        var iterations = ResolveForEachItems(stage, context, document.FilePath);
        var results = new List<IReadOnlyDictionary<string, string>>();
        var resultsJson = new List<JsonElement>();
        var workflowResults = new List<IReadOnlyDictionary<string, string>>();
        var workflowResultsJson = new List<JsonElement>();

        if (!ShouldExecuteForEachSequentially(stage))
        {
            var iterationsWithIndex = iterations.Select((item, index) => new { item, index }).ToArray();
            var tasks = iterationsWithIndex
                .Select(iteration => ExecuteWorkflowForEachIterationAsync(
                    document,
                    stage,
                    catalogVersion,
                    environment,
                    context,
                    varsOverrideActive,
                    mocked,
                    verbose,
                    debug,
                    cancellationToken,
                    iteration.item,
                    iteration.index))
                .ToArray();

            var iterationResults = await Task.WhenAll(tasks);
            foreach (var iterationResult in iterationResults.OrderBy(result => result.Index))
            {
                results.Add(iterationResult.Output);
                resultsJson.Add(iterationResult.OutputJson);
                workflowResults.Add(iterationResult.WorkflowResult);
                workflowResultsJson.Add(iterationResult.WorkflowResultJson);
            }

            ApplyLatestWorkflowIterationState(stage, context, iterationResults);
            ApplyForEachWorkflowOutputs(stage, context, results, resultsJson, workflowResults, workflowResultsJson);
            return;
        }

        foreach (var iteration in iterations.Select((item, index) => new { item, index }))
        {
            using var scope = BeginIterationScope(stage, context, iteration.item, iteration.index);
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

            if (context.WorkflowOutputs.TryGetValue(stage.Name, out var workflowOutput))
            {
                results.Add(new Dictionary<string, string>(workflowOutput, StringComparer.OrdinalIgnoreCase));
                resultsJson.Add(JsonSerializer.SerializeToElement(workflowOutput));
            }

            if (context.WorkflowResults.TryGetValue(stage.Name, out var workflowResult))
            {
                workflowResults.Add(new Dictionary<string, string>(workflowResult, StringComparer.OrdinalIgnoreCase));
                workflowResultsJson.Add(JsonSerializer.SerializeToElement(workflowResult));
            }
        }

        ApplyForEachWorkflowOutputs(stage, context, results, resultsJson, workflowResults, workflowResultsJson);
    }

    private async Task<string?> ExecuteEndpointStageWithOptionalForEachAsync(
        WorkflowDefinition definition,
        WorkflowStageDefinition stage,
        IReadOnlyDictionary<string, string> apiBaseUrls,
        ExecutionContext context,
        bool verbose,
        string workflowPath,
        bool mocked,
        CancellationToken cancellationToken)
    {
        if (!HasForEach(stage))
        {
            return await ExecuteEndpointStageAsync(definition, stage, apiBaseUrls, context, verbose, workflowPath, mocked, cancellationToken);
        }

        var iterations = ResolveForEachItems(stage, context, workflowPath);
        var results = new List<IReadOnlyDictionary<string, string>>();
        var resultsJson = new List<JsonElement>();

        if (!ShouldExecuteForEachSequentially(stage))
        {
            var iterationsWithIndex = iterations.Select((item, index) => new { item, index }).ToArray();
            var tasks = iterationsWithIndex
                .Select(iteration => ExecuteEndpointForEachIterationAsync(
                    definition,
                    stage,
                    apiBaseUrls,
                    context,
                    verbose,
                    workflowPath,
                    mocked,
                    cancellationToken,
                    iteration.item,
                    iteration.index))
                .ToArray();

            var iterationResults = await Task.WhenAll(tasks);
            foreach (var iterationResult in iterationResults.OrderBy(result => result.Index))
            {
                results.Add(iterationResult.Output);
                resultsJson.Add(iterationResult.OutputJson);
            }

            ApplyLatestEndpointIterationState(stage, context, iterationResults);
            ApplyForEachEndpointOutputs(stage, context, results, resultsJson);
            return iterationResults
                .OrderBy(result => result.Index)
                .Select(result => result.JumpTarget)
                .FirstOrDefault(jumpTarget => !string.IsNullOrWhiteSpace(jumpTarget));
        }

        foreach (var iteration in iterations.Select((item, index) => new { item, index }))
        {
            using var scope = BeginIterationScope(stage, context, iteration.item, iteration.index);
            var jumpTarget = await ExecuteEndpointStageAsync(definition, stage, apiBaseUrls, context, verbose, workflowPath, mocked, cancellationToken);
            if (context.EndpointOutputs.TryGetValue(stage.Name, out var output))
            {
                results.Add(new Dictionary<string, string>(output, StringComparer.OrdinalIgnoreCase));
                resultsJson.Add(JsonSerializer.SerializeToElement(output));
            }

            if (!string.IsNullOrWhiteSpace(jumpTarget))
            {
                ApplyForEachEndpointOutputs(stage, context, results, resultsJson);
                return jumpTarget;
            }
        }

        ApplyForEachEndpointOutputs(stage, context, results, resultsJson);
        return null;
    }

    private bool ShouldExecuteForEachSequentially(WorkflowStageDefinition stage)
        => stage.ForEachSequential == true;

    private async Task<WorkflowForEachIterationResult> ExecuteWorkflowForEachIterationAsync(
        WorkflowDocument document,
        WorkflowStageDefinition stage,
        ApiCatalogVersion catalogVersion,
        string environment,
        ExecutionContext context,
        bool varsOverrideActive,
        bool mocked,
        bool verbose,
        bool debug,
        CancellationToken cancellationToken,
        JsonElement item,
        int index)
    {
        var iterationContext = CreateIterationContext(context, stage, item, index);
        if (mocked && stage.Mock is not null)
        {
            ApplyWorkflowMock(stage, iterationContext);
            ApplyWorkflowStageResult(iterationContext, stage.Name, WorkflowResultStatus.Ok, string.Empty);
        }
        else
        {
            await ExecuteWorkflowStageAsync(
                document,
                stage,
                catalogVersion,
                environment,
                iterationContext,
                varsOverrideActive,
                mocked,
                verbose,
                debug,
                cancellationToken);
        }

        var output = iterationContext.WorkflowOutputs.TryGetValue(stage.Name, out var workflowOutput)
            ? new Dictionary<string, string>(workflowOutput, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var outputJson = iterationContext.WorkflowOutputsJson.TryGetValue(stage.Name, out var workflowOutputJson)
            ? JsonSerializer.SerializeToElement(workflowOutputJson)
            : JsonSerializer.SerializeToElement(new Dictionary<string, object?>());
        var workflowResult = iterationContext.WorkflowResults.TryGetValue(stage.Name, out var result)
            ? new Dictionary<string, string>(result, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var workflowResultJson = JsonSerializer.SerializeToElement(workflowResult);
        return new WorkflowForEachIterationResult(index, output, outputJson, workflowResult, workflowResultJson, iterationContext.SecretValues);
    }

    private async Task<EndpointForEachIterationResult> ExecuteEndpointForEachIterationAsync(
        WorkflowDefinition definition,
        WorkflowStageDefinition stage,
        IReadOnlyDictionary<string, string> apiBaseUrls,
        ExecutionContext context,
        bool verbose,
        string workflowPath,
        bool mocked,
        CancellationToken cancellationToken,
        JsonElement item,
        int index)
    {
        var iterationContext = CreateIterationContext(context, stage, item, index);
        var jumpTarget = await ExecuteEndpointStageAsync(definition, stage, apiBaseUrls, iterationContext, verbose, workflowPath, mocked, cancellationToken);
        var output = iterationContext.EndpointOutputs.TryGetValue(stage.Name, out var endpointOutput)
            ? new Dictionary<string, string>(endpointOutput, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var outputJson = JsonSerializer.SerializeToElement(output);
        return new EndpointForEachIterationResult(index, output, outputJson, jumpTarget, iterationContext.SecretValues);
    }

    private ExecutionContext CreateIterationContext(ExecutionContext source, WorkflowStageDefinition stage, JsonElement item, int index)
    {
        var iterationContext = new ExecutionContext(source.Inputs, source.EnvironmentVariables, source.Context, source.IndentLevel, source.ReportSync)
        {
            Report = source.Report,
            WorkflowVars = source.WorkflowVars
        };

        CopyJsonDictionary(source.InputJson, iterationContext.InputJson);
        CopyStringDictionary(source.Globals, iterationContext.Globals);
        CopyJsonDictionary(source.GlobalJson, iterationContext.GlobalJson);
        CopyJsonDictionary(source.ContextJson, iterationContext.ContextJson);
        CopyNestedStringDictionary(source.EndpointOutputs, iterationContext.EndpointOutputs);
        CopyNestedJsonDictionary(source.EndpointOutputsJson, iterationContext.EndpointOutputsJson);
        CopyNestedStringDictionary(source.WorkflowOutputs, iterationContext.WorkflowOutputs);
        CopyNestedJsonDictionary(source.WorkflowOutputsJson, iterationContext.WorkflowOutputsJson);
        CopyNestedStringDictionary(source.WorkflowResults, iterationContext.WorkflowResults);
        CopyCircuitBreakers(source.CircuitBreakers, iterationContext.CircuitBreakers);
        foreach (var secretValue in source.SecretValues)
        {
            iterationContext.SecretValues.Add(secretValue);
        }

        var itemName = string.IsNullOrWhiteSpace(stage.ItemName) ? "item" : stage.ItemName!;
        var indexName = string.IsNullOrWhiteSpace(stage.IndexName) ? "index" : stage.IndexName!;
        iterationContext.Context[itemName] = JsonValueHelper.ToDisplayString(item);
        iterationContext.ContextJson[itemName] = item.Clone();
        iterationContext.Context[indexName] = index.ToString();
        iterationContext.ContextJson[indexName] = JsonSerializer.SerializeToElement(index);
        return iterationContext;
    }

    private static void CopyStringDictionary(IReadOnlyDictionary<string, string> source, IDictionary<string, string> target)
    {
        foreach (var pair in source)
        {
            target[pair.Key] = pair.Value;
        }
    }

    private static void CopyJsonDictionary(IReadOnlyDictionary<string, JsonElement> source, IDictionary<string, JsonElement> target)
    {
        foreach (var pair in source)
        {
            target[pair.Key] = pair.Value.Clone();
        }
    }

    private static void CopyNestedStringDictionary(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> source,
        IDictionary<string, IReadOnlyDictionary<string, string>> target)
    {
        foreach (var pair in source)
        {
            target[pair.Key] = pair.Value;
        }
    }

    private static void CopyNestedJsonDictionary(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>> source,
        IDictionary<string, IReadOnlyDictionary<string, JsonElement>> target)
    {
        foreach (var pair in source)
        {
            target[pair.Key] = pair.Value;
        }
    }

    private static void CopyCircuitBreakers(
        IReadOnlyDictionary<string, CircuitBreakerState> source,
        IDictionary<string, CircuitBreakerState> target)
    {
        foreach (var pair in source)
        {
            target[pair.Key] = new CircuitBreakerState
            {
                ConsecutiveFailures = pair.Value.ConsecutiveFailures,
                ConsecutiveSuccesses = pair.Value.ConsecutiveSuccesses,
                OpenUntil = pair.Value.OpenUntil,
                HalfOpen = pair.Value.HalfOpen
            };
        }
    }

    private void ApplyLatestWorkflowIterationState(
        WorkflowStageDefinition stage,
        ExecutionContext context,
        IReadOnlyList<WorkflowForEachIterationResult> iterationResults)
    {
        var lastResult = iterationResults
            .OrderBy(result => result.Index)
            .LastOrDefault();
        if (lastResult is null)
        {
            return;
        }

        context.WorkflowOutputs[stage.Name] = lastResult.Output;
        context.WorkflowOutputsJson[stage.Name] = BuildJsonMap(lastResult.Output);
        context.WorkflowResults[stage.Name] = lastResult.WorkflowResult;
        foreach (var secretValue in iterationResults.SelectMany(result => result.SecretValues).Distinct(StringComparer.Ordinal))
        {
            context.SecretValues.Add(secretValue);
        }

        UpdateActiveWorkflowStageRecord(
            context,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            context.WorkflowOutputs[stage.Name],
            context.WorkflowResults[stage.Name]);
    }

    private void ApplyLatestEndpointIterationState(
        WorkflowStageDefinition stage,
        ExecutionContext context,
        IReadOnlyList<EndpointForEachIterationResult> iterationResults)
    {
        var lastResult = iterationResults
            .OrderBy(result => result.Index)
            .LastOrDefault();
        if (lastResult is null)
        {
            return;
        }

        context.EndpointOutputs[stage.Name] = lastResult.Output;
        context.EndpointOutputsJson[stage.Name] = BuildJsonMap(lastResult.Output);
        foreach (var secretValue in iterationResults.SelectMany(result => result.SecretValues).Distinct(StringComparer.Ordinal))
        {
            context.SecretValues.Add(secretValue);
        }

        var statusCode = lastResult.Output.TryGetValue("http_status", out var statusValue) &&
            int.TryParse(statusValue, out var parsedStatus)
            ? parsedStatus
            : 0;
        UpdateActiveStageRecordOutput(context, context.EndpointOutputs[stage.Name], statusCode);
    }

    private bool HasForEach(WorkflowStageDefinition stage)
        => !string.IsNullOrWhiteSpace(stage.ForEach) || !string.IsNullOrWhiteSpace(stage.DataFile);

    private WorkflowExecutionReport BuildExecutionReport(
        WorkflowDocument document,
        string environment,
        IReadOnlyDictionary<string, string> inputs,
        bool mocked,
        WorkflowPreflightReport? preflightReport)
    {
        var secretInputKeys = document.Definition.Input?
            .Where(i => i.Secret)
            .Select(i => i.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var reportInputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in inputs)
        {
            reportInputs[pair.Key] = secretInputKeys?.Contains(pair.Key) == true ? "*****" : pair.Value;
        }

        return new WorkflowExecutionReport
        {
            WorkflowName = document.Definition.Name,
            WorkflowId = document.Definition.Id,
            WorkflowVersion = document.Definition.Version,
            ToolVersion = typeof(WorkflowExecutor).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion?.Split('+')[0]
                ?? typeof(WorkflowExecutor).Assembly.GetName().Version?.ToString()
                ?? string.Empty,
            WorkflowPath = document.FilePath,
            Environment = environment,
            Mocked = mocked,
            DryRun = false,
            StartedAtUtc = _systemProvider.UtcNow,
            Inputs = reportInputs,
            Preflight = preflightReport ?? new WorkflowPreflightReport()
        };
    }

    private async Task<WorkflowExecutionResult> FinalizeExecutionResultAsync(
        WorkflowDocument document,
        ExecutionContext context,
        CancellationToken cancellationToken,
        bool success,
        string? errorMessage)
    {
        var outputs = context.WorkflowOutputs.TryGetValue(document.Definition.Name, out var workflowOutputs)
            ? workflowOutputs
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var endStageSecretKeys = document.Definition.EndStage?.SecretOutputs is { Count: > 0 }
            ? new HashSet<string>(document.Definition.EndStage.SecretOutputs, StringComparer.OrdinalIgnoreCase)
            : null;
        var redactedOutputs = WorkflowExecutionRedactor.RedactOutputStrings(outputs, endStageSecretKeys, context.SecretValues);

        if (context.Report is not null)
        {
            context.Report.Result = success ? "Ok" : "Error";
            context.Report.ErrorMessage = errorMessage;
            context.Report.FinishedAtUtc = _systemProvider.UtcNow;
            context.Report.DurationMs = (long)(context.Report.FinishedAtUtc.Value - context.Report.StartedAtUtc).TotalMilliseconds;
            context.Report.Output = WorkflowExecutionRedactor.ConvertOutputs(outputs, endStageSecretKeys, context.SecretValues);
            context.Report.OutputFilePath = context.OutputFilePath;
            context.Report.Metrics.TotalStages = context.Report.Stages.Count;
            context.Report.Metrics.PreflightRetries = context.Report.Preflight.TotalRetries;
        }

        var artifacts = context.Report is null
            ? new WorkflowExecutionArtifacts(null, null)
            : await _reportWriter.WriteAsync(context.Report, document, _reportOptions, cancellationToken);

        return new WorkflowExecutionResult(redactedOutputs, context.OutputFilePath, artifacts.JsonReportPath, artifacts.HtmlReportPath, context.Report?.ExecutionId);
    }

    private WorkflowStageExecutionRecord BeginStageRecord(string workflowName, WorkflowStageDefinition stage, ExecutionContext context, bool isMocked)
    {
        var record = new WorkflowStageExecutionRecord
        {
            WorkflowName = workflowName,
            StageName = stage.Name,
            StageKind = stage.Kind,
            Depth = context.IndentLevel,
            RunIf = stage.RunIf,
            Mocked = isMocked,
            EnsureMode = stage.Ensure?.Mode,
            DelaySeconds = stage.DelaySeconds > 0 ? stage.DelaySeconds : null,
            ForEachExecutionMode = HasForEach(stage)
                ? (ShouldExecuteForEachSequentially(stage) ? "Sequential" : "Parallel")
                : null,
            StartedAtUtc = _systemProvider.UtcNow
        };
        if (context.Report is not null)
        {
            lock (context.ReportSync)
            {
                context.Report.Stages.Add(record);
            }
        }

        context.ActiveStageRecord = record;
        return record;
    }

    private void CompleteStageRecord(WorkflowStageExecutionRecord stageRecord, ExecutionContext context, string status, string? jumpTarget, string? errorMessage)
    {
        stageRecord.Status = status;
        stageRecord.JumpTarget = jumpTarget;
        stageRecord.ErrorMessage = errorMessage;
        stageRecord.FinishedAtUtc = _systemProvider.UtcNow;
        stageRecord.DurationMs = (long)(stageRecord.FinishedAtUtc.Value - stageRecord.StartedAtUtc).TotalMilliseconds;
        context.ActiveStageRecord = null;

        if (context.Report is null)
        {
            return;
        }

        lock (context.ReportSync)
        {
            switch (status)
            {
                case "Skipped":
                    context.Report.Metrics.SkippedStages++;
                    break;
                case "Error":
                    context.Report.Metrics.FailedStages++;
                    context.Report.Metrics.ExecutedStages++;
                    break;
                default:
                    context.Report.Metrics.ExecutedStages++;
                    break;
            }

            if (stageRecord.Mocked)
            {
                context.Report.Metrics.MockedStages++;
            }

            if (!WorkflowStageKind.IsWorkflow(stageRecord.StageKind))
            {
                context.Report.Metrics.HttpStages++;
            }
            else
            {
                context.Report.Metrics.WorkflowStages++;
            }

            if (!string.IsNullOrWhiteSpace(jumpTarget))
            {
                context.Report.Metrics.JumpedStages++;
            }

            context.Report.Metrics.TotalRetries += stageRecord.RetryCount;
        }
    }

    private void RecordSkippedStage(string workflowName, WorkflowStageDefinition stage, ExecutionContext context)
    {
        context.SkippedStages.Add(stage.Name);

        // Mejora 5: register onSkip.output so downstream stages can reference a stable key
        if (stage.OnSkip?.Output is { Count: > 0 })
        {
            var templateContext = context.BuildTemplateContext();
            var onSkipOutput = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var onSkipOutputJson = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in stage.OnSkip.Output)
            {
                var resolved = _templateResolver.ResolveTemplate(value, templateContext);
                onSkipOutput[key] = resolved;
                AssignJsonValue(onSkipOutputJson, key, resolved);
            }

            context.EndpointOutputs[stage.Name] = onSkipOutput;
            context.EndpointOutputsJson[stage.Name] = onSkipOutputJson;
        }

        var record = new WorkflowStageExecutionRecord
        {
            WorkflowName = workflowName,
            StageName = stage.Name,
            StageKind = stage.Kind,
            Depth = context.IndentLevel,
            Status = "Skipped",
            RunIf = stage.RunIf,
            ForEachExecutionMode = HasForEach(stage)
                ? (ShouldExecuteForEachSequentially(stage) ? "Sequential" : "Parallel")
                : null,
            StartedAtUtc = _systemProvider.UtcNow,
            FinishedAtUtc = _systemProvider.UtcNow,
            DurationMs = 0
        };
        if (context.Report is not null)
        {
            lock (context.ReportSync)
            {
                context.Report.Stages.Add(record);
            }
        }
        if (context.Report is not null)
        {
            context.Report.Metrics.SkippedStages++;
            if (!WorkflowStageKind.IsWorkflow(record.StageKind))
            {
                context.Report.Metrics.HttpStages++;
            }
            else
            {
                context.Report.Metrics.WorkflowStages++;
            }
        }
    }

    private IReadOnlyList<JsonElement> ResolveForEachItems(WorkflowStageDefinition stage, ExecutionContext context, string workflowPath)
    {
        JsonElement source;
        if (!string.IsNullOrWhiteSpace(stage.DataFile))
        {
            var templateContext = context.BuildTemplateContext(workflowPath);
            string resolvedDataFile;
            string rawContent;
            try
            {
                resolvedDataFile = WorkflowReferencePathResolver.ResolvePath(stage.DataFile, templateContext);
                rawContent = _dataFileService.LoadText(resolvedDataFile, workflowPath);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Stage '{stage.Name}' dataFile '{stage.DataFile}' could not be loaded: {ex.Message}",
                    ex);
            }

            var resolvedContent = _templateResolver.ResolveTemplate(rawContent, context.BuildTemplateContext(workflowPath));
            source = _dataFileService.ParseStructured(resolvedContent, resolvedDataFile);
            if (!string.IsNullOrWhiteSpace(stage.ForEach))
            {
                var path = stage.ForEach.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (!JsonValueHelper.TryResolvePath(source, path, out source))
                {
                    throw new InvalidOperationException($"Stage '{stage.Name}' forEach path '{stage.ForEach}' was not found in dataFile.");
                }
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(stage.ForEach))
            {
                throw new InvalidOperationException($"Stage '{stage.Name}' forEach requires a source expression.");
            }

            var token = ExtractSingleToken(stage.ForEach);
            var value = _templateResolver.ResolveTokenValue(token, context.BuildTemplateContext(), null, allowJsonStage: true);
            if (!value.JsonValue.HasValue)
            {
                throw new InvalidOperationException($"Stage '{stage.Name}' forEach source must resolve to a JSON array.");
            }

            source = value.JsonValue.Value;
        }

        if (source.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Stage '{stage.Name}' forEach source must be a JSON array.");
        }

        return source.EnumerateArray().Select(item => item.Clone()).ToArray();
    }

    private IterationScope BeginIterationScope(WorkflowStageDefinition stage, ExecutionContext context, JsonElement item, int index)
    {
        var itemName = string.IsNullOrWhiteSpace(stage.ItemName) ? "item" : stage.ItemName;
        var indexName = string.IsNullOrWhiteSpace(stage.IndexName) ? "index" : stage.IndexName;
        return new IterationScope(context, itemName!, indexName!, item, index);
    }

    private void ApplyForEachEndpointOutputs(
        WorkflowStageDefinition stage,
        ExecutionContext context,
        List<IReadOnlyDictionary<string, string>> results,
        List<JsonElement> resultsJson)
    {
        if (!context.EndpointOutputs.TryGetValue(stage.Name, out var current))
        {
            current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var aggregated = new Dictionary<string, string>(current, StringComparer.OrdinalIgnoreCase)
        {
            ["foreach_count"] = results.Count.ToString()
        };
        aggregated["foreach_items"] = JsonSerializer.Serialize(results);
        context.EndpointOutputs[stage.Name] = aggregated;

        var aggregatedJson = context.EndpointOutputsJson.TryGetValue(stage.Name, out var jsonCurrent)
            ? new Dictionary<string, JsonElement>(jsonCurrent, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        aggregatedJson["foreach_items"] = JsonSerializer.SerializeToElement(resultsJson);
        context.EndpointOutputsJson[stage.Name] = aggregatedJson;
    }

    private void ApplyForEachWorkflowOutputs(
        WorkflowStageDefinition stage,
        ExecutionContext context,
        List<IReadOnlyDictionary<string, string>> results,
        List<JsonElement> resultsJson,
        List<IReadOnlyDictionary<string, string>> workflowResults,
        List<JsonElement> workflowResultsJson)
    {
        if (!context.WorkflowOutputs.TryGetValue(stage.Name, out var current))
        {
            current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var failedCount = workflowResults.Count(result =>
            result.TryGetValue("status", out var status) &&
            string.Equals(status, WorkflowResultStatus.Error.ToString(), StringComparison.OrdinalIgnoreCase));
        var aggregated = new Dictionary<string, string>(current, StringComparer.OrdinalIgnoreCase)
        {
            ["foreach_count"] = results.Count.ToString(),
            ["foreach_items"] = JsonSerializer.Serialize(results),
            ["foreach_results"] = JsonSerializer.Serialize(workflowResults),
            ["foreach_success_count"] = (workflowResults.Count - failedCount).ToString(),
            ["foreach_failed_count"] = failedCount.ToString()
        };
        context.WorkflowOutputs[stage.Name] = aggregated;

        var aggregatedJson = context.WorkflowOutputsJson.TryGetValue(stage.Name, out var jsonCurrent)
            ? new Dictionary<string, JsonElement>(jsonCurrent, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        aggregatedJson["foreach_items"] = JsonSerializer.SerializeToElement(resultsJson);
        aggregatedJson["foreach_results"] = JsonSerializer.SerializeToElement(workflowResultsJson);
        context.WorkflowOutputsJson[stage.Name] = aggregatedJson;
    }

    private void ApplyTypedInputs(WorkflowDefinition definition, ExecutionContext context)
    {
        if (definition.Input is null)
        {
            return;
        }

        foreach (var input in definition.Input)
        {
            if (!context.Inputs.TryGetValue(input.Name, out var rawValue))
            {
                continue;
            }

            if (input.Type is RandomValueType.Object or RandomValueType.Array)
            {
                if (!JsonValueHelper.TryParse(rawValue, out var json))
                {
                    throw new InvalidOperationException($"Input '{input.Name}' must contain valid JSON for type '{input.Type}'.");
                }

                context.InputJson[input.Name] = json;
            }
            else
            {
                AssignJsonValue(context.InputJson, input.Name, rawValue);
            }
        }
    }

    private static void CopyJsonContext(ExecutionContext source, ExecutionContext target)
    {
        foreach (var pair in source.ContextJson)
        {
            target.ContextJson[pair.Key] = pair.Value.Clone();
        }
    }

    private static IReadOnlyDictionary<string, JsonElement> BuildJsonMap(IReadOnlyDictionary<string, string> values)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values)
        {
            AssignJsonValue(result, pair.Key, pair.Value);
        }

        return result;
    }

    private static void AssignJsonValue(IDictionary<string, JsonElement> target, string key, string value)
    {
        if (JsonValueHelper.TryParse(value, out var json))
        {
            target[key] = json;
        }
    }

    private void UpdateActiveStageRecordForResponse(
        ExecutionContext context,
        WorkflowStageDefinition stage,
        ResponseContext responseContext,
        string? requestUri,
        string? httpMethod,
        int retriesUsed,
        string workflowPath,
        bool mocked,
        string? requestBody = null)
    {
        var record = context.ActiveStageRecord;
        if (record is null)
        {
            return;
        }

        record.Mocked = mocked;
        record.HttpStatusCode = responseContext.StatusCode;
        record.RequestUri = requestUri;
        record.HttpMethod = httpMethod ?? stage.HttpVerb;
        record.RetryCount = retriesUsed;
        record.EnsureStatus = stage.Ensure is null
            ? null
            : ((stage.Ensure.ExistsOn is { Length: > 0 } ? stage.Ensure.ExistsOn : new[] { 409 }).Contains(responseContext.StatusCode)
                ? "existing"
                : "created");

        if (_reportOptions.CaptureHttp == ExecutionHttpCaptureMode.None)
        {
            return;
        }

        if (_reportOptions.CaptureHttp is ExecutionHttpCaptureMode.Headers or ExecutionHttpCaptureMode.Bodies)
        {
            record.ResponseHeaders = WorkflowExecutionRedactor.RedactHeaders(responseContext.Headers, _reportOptions.RedactSensitiveData);
        }

        if (_reportOptions.CaptureHttp == ExecutionHttpCaptureMode.Bodies)
        {
            record.RequestHeaders = ExtractRequestHeaders(stage, context.BuildTemplateContext(workflowPath));
            record.RequestBody = WorkflowExecutionRedactor.RedactBody(requestBody, _reportOptions.RedactSensitiveData);
            record.ResponseBody = WorkflowExecutionRedactor.RedactBody(responseContext.Body, _reportOptions.RedactSensitiveData);
        }
    }

    private void UpdateActiveStageRecordOutput(ExecutionContext context, IReadOnlyDictionary<string, string> stageOutput, int statusCode, IReadOnlySet<string>? secretOutputKeys = null)
    {
        var record = context.ActiveStageRecord;
        if (record is null)
        {
            return;
        }

        record.HttpStatusCode = statusCode;
        record.Output = WorkflowExecutionRedactor.ConvertOutputs(stageOutput, secretOutputKeys, context.SecretValues);
        if (stageOutput.TryGetValue("ensure_status", out var ensureStatus))
        {
            record.EnsureStatus = ensureStatus;
        }
    }

    private void UpdateActiveWorkflowStageRecord(
        ExecutionContext context,
        IReadOnlyDictionary<string, string> workflowInputs,
        IReadOnlyDictionary<string, string> workflowOutput,
        IReadOnlyDictionary<string, string> workflowResult)
    {
        var record = context.ActiveStageRecord;
        if (record is null)
        {
            return;
        }

        record.WorkflowInputs = WorkflowExecutionRedactor.ConvertOutputs(workflowInputs, secretValues: context.SecretValues);
        record.WorkflowOutput = WorkflowExecutionRedactor.ConvertOutputs(workflowOutput, secretValues: context.SecretValues);
        record.WorkflowResult = WorkflowExecutionRedactor.ConvertOutputs(workflowResult, secretValues: context.SecretValues);
    }

    private Dictionary<string, string>? ExtractRequestHeaders(WorkflowStageDefinition stage, TemplateContext templateContext)
    {
        if (stage.Headers is null || stage.Headers.Count == 0)
        {
            return null;
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in stage.Headers)
        {
            headers[pair.Key] = _templateResolver.ResolveTemplate(pair.Value, templateContext);
        }

        return WorkflowExecutionRedactor.RedactHeaders(headers, _reportOptions.RedactSensitiveData);
    }

    private static bool TryResolveStatusAction(WorkflowStageDefinition stage, int statusCode, out WorkflowStageStatusAction statusAction)
    {
        statusAction = new WorkflowStageStatusAction();
        if (stage.OnStatus is not null && stage.OnStatus.TryGetValue(statusCode, out var configured))
        {
            statusAction = configured;
            return true;
        }

        if (TryResolveEnsureStatusAction(stage, statusCode, out var ensureAction))
        {
            statusAction = ensureAction;
            return true;
        }

        if (stage.JumpOnStatus is not null && stage.JumpOnStatus.TryGetValue(statusCode, out var jumpTarget))
        {
            statusAction = new WorkflowStageStatusAction { JumpTo = jumpTarget };
            return true;
        }

        return false;
    }

    private void ApplyStatusActionOutput(
        WorkflowStageStatusAction statusAction,
        WorkflowStageDefinition stage,
        ExecutionContext context,
        ResponseContext responseContext,
        IDictionary<string, string> stageOutput,
        IDictionary<string, JsonElement> stageOutputJson)
    {
        if (statusAction.Output is not null)
        {
            foreach (var pair in statusAction.Output)
            {
                var resolved = _templateResolver.ResolveTemplate(pair.Value, context.BuildTemplateContext(), responseContext);
                stageOutput[pair.Key] = resolved;
                AssignJsonValue(stageOutputJson, pair.Key, resolved);
            }
        }

        if (!string.IsNullOrWhiteSpace(statusAction.Message))
        {
            stage.Message = statusAction.Message;
        }
    }

    private static bool IsExpectedStatus(WorkflowStageDefinition stage, int statusCode)
    {
        var expectedStatuses = BuildAllowedStatuses(stage);
        if (expectedStatuses.Count > 0)
        {
            return expectedStatuses.Contains(statusCode);
        }

        return true;
    }

    private static InvalidOperationException BuildUnexpectedStatusException(WorkflowStageDefinition stage, int statusCode)
    {
        var expectedStatuses = BuildAllowedStatuses(stage);
        if (expectedStatuses.Count > 0)
        {
            return new InvalidOperationException(
                $"Stage '{stage.Name}' returned {statusCode} but expected one of [{string.Join(", ", expectedStatuses.Order())}].");
        }

        return new InvalidOperationException(
            $"Stage '{stage.Name}' returned {statusCode} but expected {stage.ExpectedStatus}.");
    }

    private static HashSet<int> BuildAllowedStatuses(WorkflowStageDefinition stage)
    {
        var statuses = new HashSet<int>();
        if (stage.ExpectedStatuses is { Length: > 0 })
        {
            foreach (var status in stage.ExpectedStatuses)
            {
                statuses.Add(status);
            }
        }
        else if (stage.ExpectedStatus.HasValue)
        {
            statuses.Add(stage.ExpectedStatus.Value);
        }

        if (stage.Ensure?.ExistsOn is { Length: > 0 })
        {
            foreach (var status in stage.Ensure.ExistsOn)
            {
                statuses.Add(status);
            }
        }
        else if (stage.Ensure is not null)
        {
            statuses.Add(409);
        }

        return statuses;
    }

    private static bool TryResolveEnsureStatusAction(WorkflowStageDefinition stage, int statusCode, out WorkflowStageStatusAction statusAction)
    {
        statusAction = new WorkflowStageStatusAction();
        if (stage.Ensure is null)
        {
            return false;
        }

        var existsStatuses = stage.Ensure.ExistsOn is { Length: > 0 }
            ? stage.Ensure.ExistsOn
            : new[] { 409 };
        if (!existsStatuses.Contains(statusCode))
        {
            return false;
        }

        var output = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ensure_status"] = "existing",
            ["ensured"] = "true",
            ["existed"] = "true"
        };
        if (stage.Ensure.Output is not null)
        {
            foreach (var pair in stage.Ensure.Output)
            {
                output[pair.Key] = pair.Value;
            }
        }

        statusAction = new WorkflowStageStatusAction
        {
            JumpTo = stage.Ensure.JumpTo,
            Message = stage.Ensure.Message,
            Output = output
        };
        return true;
    }

    private static void ApplyEnsureOutputs(WorkflowStageDefinition stage, int statusCode, IDictionary<string, string> stageOutput, IDictionary<string, JsonElement> stageOutputJson)
    {
        if (stage.Ensure is null)
        {
            return;
        }

        var existsStatuses = stage.Ensure.ExistsOn is { Length: > 0 }
            ? stage.Ensure.ExistsOn
            : new[] { 409 };
        var existed = existsStatuses.Contains(statusCode);
        stageOutput["ensure_status"] = existed ? "existing" : "created";
        stageOutput["ensured"] = "true";
        stageOutput["existed"] = existed ? "true" : "false";
        AssignJsonValue(stageOutputJson, "ensure_status", stageOutput["ensure_status"]);
    }

    private static string ExtractSingleToken(string template)
    {
        var tokens = TemplateResolver.ExtractTokens(template).ToArray();
        if (tokens.Length != 1)
        {
            throw new InvalidOperationException($"ForEach expression '{template}' must contain exactly one template token.");
        }

        return tokens[0];
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

        string rawPayload;
        try
        {
            rawPayload = string.IsNullOrWhiteSpace(stage.Mock?.PayloadFile)
                ? _mockPayloadService.LoadRawPayload(stage.Mock?.Payload ?? string.Empty, workflowPath)
                : _mockPayloadService.LoadRawPayloadFromFile(stage.Mock.PayloadFile, context.BuildTemplateContext(workflowPath));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Stage '{stage.Name}' mock payload source could not be loaded: {ex.Message}",
                ex);
        }

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

        var status = stage.Mock?.Status ?? stage.ExpectedStatus ?? stage.ExpectedStatuses?.FirstOrDefault() ?? 200;
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
            RegisterSecretInputValues(definition, context);
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
            AssignJsonValue(context.GlobalJson, variable.Name, value);
            if (variable.Secret && !string.IsNullOrEmpty(value))
            {
                context.SecretValues.Add(value);
            }
        }

        RegisterSecretInputValues(definition, context);
        ApplyInitContext(definition, context);
    }

    private static void RegisterSecretInputValues(WorkflowDefinition definition, ExecutionContext context)
    {
        if (definition.Input is null)
        {
            return;
        }

        foreach (var input in definition.Input)
        {
            if (input.Secret && context.Inputs.TryGetValue(input.Name, out var value) && !string.IsNullOrEmpty(value))
            {
                context.SecretValues.Add(value);
            }
        }
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
            AssignJsonValue(context.ContextJson, pair.Key, resolved);
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
            int indentLevel = 0,
            object? reportSync = null)
        {
            Inputs = new Dictionary<string, string>(inputs, StringComparer.OrdinalIgnoreCase);
            InputJson = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            EnvironmentVariables = new Dictionary<string, string>(environmentVariables, StringComparer.OrdinalIgnoreCase);
            Globals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            GlobalJson = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            EndpointOutputs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            EndpointOutputsJson = new Dictionary<string, IReadOnlyDictionary<string, JsonElement>>(StringComparer.OrdinalIgnoreCase);
            WorkflowOutputs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            WorkflowOutputsJson = new Dictionary<string, IReadOnlyDictionary<string, JsonElement>>(StringComparer.OrdinalIgnoreCase);
            WorkflowResults = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            CircuitBreakers = new Dictionary<string, CircuitBreakerState>(StringComparer.OrdinalIgnoreCase);
            SecretValues = new HashSet<string>(StringComparer.Ordinal);
            SkippedStages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Context = parentContext is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(parentContext, StringComparer.OrdinalIgnoreCase);
            ContextJson = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            IndentLevel = Math.Max(0, indentLevel);
            ReportSync = reportSync ?? new object();
        }

        public Dictionary<string, string> Inputs { get; }
        public Dictionary<string, JsonElement> InputJson { get; }
        public Dictionary<string, string> EnvironmentVariables { get; }
        public Dictionary<string, string> Globals { get; }
        public Dictionary<string, JsonElement> GlobalJson { get; }
        public Dictionary<string, string> Context { get; }
        public Dictionary<string, JsonElement> ContextJson { get; }
        public Dictionary<string, IReadOnlyDictionary<string, string>> EndpointOutputs { get; }
        public Dictionary<string, IReadOnlyDictionary<string, JsonElement>> EndpointOutputsJson { get; }
        public Dictionary<string, IReadOnlyDictionary<string, string>> WorkflowOutputs { get; }
        public Dictionary<string, IReadOnlyDictionary<string, JsonElement>> WorkflowOutputsJson { get; }
        public Dictionary<string, IReadOnlyDictionary<string, string>> WorkflowResults { get; }
        public Dictionary<string, CircuitBreakerState> CircuitBreakers { get; }
        public HashSet<string> SecretValues { get; }
        public HashSet<string> SkippedStages { get; }
        public IReadOnlyDictionary<string, string>? WorkflowVars { get; set; }
        public string? OutputFilePath { get; set; }
        public int IndentLevel { get; }
        public WorkflowExecutionReport? Report { get; set; }
        public WorkflowStageExecutionRecord? ActiveStageRecord { get; set; }
        public object ReportSync { get; }

        public TemplateContext BuildTemplateContext(string? workflowPath = null)
        {
            return new TemplateContext(
                Inputs,
                Globals,
                Context,
                EndpointOutputs,
                WorkflowOutputs,
                WorkflowResults,
                EnvironmentVariables,
                InputJson,
                GlobalJson,
                ContextJson,
                EndpointOutputsJson,
                WorkflowOutputsJson,
                workflowPath,
                SkippedStages,
                WorkflowVars);
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
                AssignJsonValue(context.GlobalJson, pair.Key, resolved);
            }
        }

        if (stage.Context is not null)
        {
            foreach (var pair in stage.Context)
            {
                var resolved = _templateResolver.ResolveTemplate(pair.Value, context.BuildTemplateContext());
                context.Context[pair.Key] = resolved;
                AssignJsonValue(context.ContextJson, pair.Key, resolved);
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
            AssignJsonValue(context.ContextJson, pair.Key, resolved);
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
        return _expressionEvaluator.Evaluate(expression, context.BuildTemplateContext());
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
        context.WorkflowOutputsJson[stage.Name] = BuildJsonMap(output);
    }

}

public sealed record WorkflowExecutionResult(
    IReadOnlyDictionary<string, string> Output,
    string? OutputFilePath,
    string? JsonReportPath,
    string? HtmlReportPath,
    string? ExecutionId);
