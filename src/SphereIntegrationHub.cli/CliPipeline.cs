using System.Diagnostics;

using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Plugins;
using SphereIntegrationHub.Services;

namespace SphereIntegrationHub.cli;

internal sealed class CliPipeline : ICliPipeline
{
    private readonly ICliPathResolver _pathResolver;
    private readonly ICliPlanPrinter _planPrinter;
    private readonly ICliWorkflowEnvironmentValidator _environmentValidator;
    private readonly ICliOutputProvider _output;
    private readonly ICliServiceFactory _serviceFactory;
    private readonly IWorkflowConfigLoader _configLoader;
    private readonly IOpenTelemetryBootstrapper _telemetryBootstrapper;

    public CliPipeline(
        ICliPathResolver pathResolver,
        ICliPlanPrinter planPrinter,
        ICliWorkflowEnvironmentValidator environmentValidator,
        ICliOutputProvider output,
        ICliServiceFactory serviceFactory,
        IWorkflowConfigLoader configLoader,
        IOpenTelemetryBootstrapper telemetryBootstrapper)
    {
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _planPrinter = planPrinter ?? throw new ArgumentNullException(nameof(planPrinter));
        _environmentValidator = environmentValidator ?? throw new ArgumentNullException(nameof(environmentValidator));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));
        _configLoader = configLoader ?? throw new ArgumentNullException(nameof(configLoader));
        _telemetryBootstrapper = telemetryBootstrapper ?? throw new ArgumentNullException(nameof(telemetryBootstrapper));
    }

    public async Task<CliRunResult> RunAsync(InlineArguments parseResult, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var messages = new List<CliRunMessage>();
        var emittedMessageCount = 0;
        var workflowPath = parseResult.WorkflowPath
            ?? throw new InvalidOperationException("Workflow path is required.");
        var config = _configLoader.Load(workflowPath);
        EmitPluginConfigurationWarning(config, messages);
        if (parseResult.Debug)
        {
            config.OpenTelemetry.DebugConsole = true;
        }
        WorkflowExecutionReportOptions reportOptions;
        try
        {
            reportOptions = WorkflowExecutionReportOptionsResolver.Resolve(config.Reporting, parseResult);
        }
        catch (Exception ex)
        {
            messages.Add(new CliRunMessage(CliRunMessageKind.Error, ex.Message));
            return new CliRunResult(1, messages, emittedMessageCount);
        }
        using var telemetryHandle = _telemetryBootstrapper.Start(config);
        using var activity = Telemetry.ActivitySource.StartActivity(TelemetryConstants.ActivityCliRun);
        activity?.SetTag(TelemetryConstants.TagWorkflowPath, parseResult.WorkflowPath);
        var preflightReport = new WorkflowPreflightReport();

        EmitPreamble(parseResult, messages);

        var catalogPath = parseResult.CatalogPath ?? _pathResolver.ResolveDefaultCatalogPath(workflowPath);
        EmitCatalogInfo(parseResult, catalogPath, messages);

        var workflowLoader = _serviceFactory.CreateWorkflowLoader();
        var secretProviderEnvironment = await ResolveSecretProviderEnvironmentAsync(config, messages, cancellationToken);
        if (secretProviderEnvironment is null)
        {
            return new CliRunResult(1, messages, emittedMessageCount);
        }

        if (!TryLoadWorkflow(parseResult, workflowLoader, secretProviderEnvironment.Secrets, messages, out var workflowDocument))
        {
            return new CliRunResult(1, messages, emittedMessageCount);
        }

        if (!TryLoadVars(parseResult, workflowDocument, out var varsOverrideActive, messages))
        {
            return new CliRunResult(1, messages, emittedMessageCount);
        }

        if (!TryLoadCatalog(catalogPath, workflowDocument, messages, out var catalog, out var selectedVersion))
        {
            return new CliRunResult(1, messages, emittedMessageCount);
        }

        StagePluginRegistry stagePluginRegistry;
        try
        {
            stagePluginRegistry = new StagePluginRegistryBuilder().Build(config, selectedVersion, workflowPath);
        }
        catch (Exception ex)
        {
            AddError(messages, $"Plugin pre-check failed: {ex.Message}");
            return new CliRunResult(1, messages, emittedMessageCount);
        }

        if (!TryValidateWorkflow(parseResult, workflowDocument, workflowLoader, stagePluginRegistry, messages))
        {
            return new CliRunResult(1, messages, emittedMessageCount);
        }

        if (!TryValidateEnvironment(parseResult, workflowDocument, selectedVersion, messages))
        {
            return new CliRunResult(1, messages, emittedMessageCount);
        }

        EmitPreflightFeatures(selectedVersion.Definitions, messages);
        EmitBaseUrlInfo(selectedVersion.Definitions, selectedVersion, parseResult.Environment!, messages);
        if (!await EmitApiHealthChecksAsync(
                selectedVersion.Definitions,
                selectedVersion,
                parseResult.Environment!,
                parseResult.Verbose,
                messages,
                preflightReport,
                cancellationToken))
        {
            emittedMessageCount = EmitPendingMessages(messages, emittedMessageCount);
            return new CliRunResult(1, messages, emittedMessageCount);
        }
        emittedMessageCount = EmitPendingMessages(messages, emittedMessageCount);

        if (!await TryCacheSwaggerAndValidateEndpoints(parseResult, workflowDocument, selectedVersion, stagePluginRegistry, messages, preflightReport, cancellationToken))
        {
            return new CliRunResult(1, messages, emittedMessageCount);
        }

        emittedMessageCount = EmitPendingMessages(messages, emittedMessageCount);

        if (parseResult.DryRun)
        {
            return BuildDryRunResult(workflowDocument, workflowLoader, parseResult, stopwatch, secretProviderEnvironment.SecretValues, messages, emittedMessageCount);
        }

        var cacheRoot = Path.Combine(_pathResolver.ResolveDefaultCacheRoot(parseResult.WorkflowPath), selectedVersion.Version);
        return await ExecuteWorkflowAsync(parseResult, workflowDocument, selectedVersion, cacheRoot, varsOverrideActive, reportOptions, preflightReport, stagePluginRegistry, secretProviderEnvironment.SecretValues, stopwatch, messages, emittedMessageCount, cancellationToken);
    }

    private void EmitPreamble(InlineArguments parseResult, List<CliRunMessage> messages)
    {
        if (parseResult.DryRun)
        {
            AddInfo(messages, "Starting dry-run...");
            AddInfo(messages, "Dry-run phases: load workflow, resolve vars, validate workflow, validate environment, health checks, swagger cache, endpoint validation, execution plan.");
        }

        if (parseResult.Mocked && !parseResult.DryRun)
        {
            AddInfo(messages, "Using mocked payloads/outputs when defined...");
        }
    }

    private void EmitCatalogInfo(InlineArguments parseResult, string catalogPath, List<CliRunMessage> messages)
    {
        AddInfo(messages, $"Catalog: {_pathResolver.FormatPath(catalogPath)}");
        AddInfo(messages, $"Workflow path: {_pathResolver.FormatPath(parseResult.WorkflowPath)}");
        AddInfo(messages, $"Environment: {parseResult.Environment}");
    }

    private static void EmitPluginConfigurationWarning(WorkflowConfig config, List<CliRunMessage> messages)
    {
        if (config.Plugins is { Count: > 0 })
        {
            return;
        }

        AddInfo(
            messages,
            "Warning: workflows.config does not define a plugins section. HTTP will be enabled by default for compatibility, but this section will be mandatory in a future release.");
    }

    private async Task<ResolvedSecretProviderEnvironment?> ResolveSecretProviderEnvironmentAsync(
        WorkflowConfig config,
        List<CliRunMessage> messages,
        CancellationToken cancellationToken)
    {
        if (config.SecretProviders is not { Count: > 0 })
        {
            return ResolvedSecretProviderEnvironment.Empty;
        }

        var registry = _serviceFactory.CreateSecretProviderRegistry();
        var processEnvironment = Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(
                entry => entry.Key.ToString() ?? string.Empty,
                entry => entry.Value?.ToString() ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);

        var resolvedSecrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var secretValues = new HashSet<string>(StringComparer.Ordinal);
        using var httpClient = _serviceFactory.CreateHttpClient();

        foreach (var provider in config.SecretProviders)
        {
            if (!registry.TryGet(provider.Plugin, out var plugin))
            {
                AddError(messages, $"Secret provider plugin '{provider.Plugin}' is not registered.");
                return null;
            }

            try
            {
                var result = await plugin.ResolveAsync(
                    provider,
                    new SecretProviderExecutionContext(httpClient, processEnvironment),
                    cancellationToken);

                foreach (var pair in result.Secrets)
                {
                    resolvedSecrets[pair.Key] = pair.Value;
                }

                foreach (var secretValue in result.SecretValues.Where(static value => !string.IsNullOrWhiteSpace(value)))
                {
                    secretValues.Add(secretValue);
                }

                AddInfo(messages, $"Secret provider '{provider.Plugin}' resolved {result.Secrets.Count} secret(s).");
            }
            catch (Exception ex)
            {
                AddError(messages, $"Secret provider '{provider.Plugin}' failed: {ex.Message}");
                return null;
            }
        }

        return new ResolvedSecretProviderEnvironment(resolvedSecrets, secretValues);
    }
    private void EmitPreflightFeatures(IReadOnlyList<ApiDefinition> definitions, List<CliRunMessage> messages)
    {
        var withHealthCheck = definitions.Where(definition => !string.IsNullOrWhiteSpace(definition.HealthCheck)).ToList();
        if (withHealthCheck.Count == 0)
        {
            return;
        }

        var withReadiness = withHealthCheck.Count(definition => definition.Readiness is not null);
        AddInfo(messages, string.Empty);
        AddInfo(messages, "Features:");
        AddInfo(messages, $"- Health check retry policy: {(withReadiness > 0 ? $"enabled for {withReadiness}/{withHealthCheck.Count} APIs" : $"not configured for {withHealthCheck.Count} APIs")}");
        AddInfo(messages, $"- Startup guard: {(withReadiness > 0 ? "enabled (aborts if APIs are unhealthy)" : "disabled")}");
    }

    private bool TryLoadWorkflow(
        InlineArguments parseResult,
        WorkflowLoader workflowLoader,
        IReadOnlyDictionary<string, string> inheritedEnvironment,
        List<CliRunMessage> messages,
        out WorkflowDocument workflowDocument)
    {
        try
        {
            workflowDocument = workflowLoader.Load(parseResult.WorkflowPath!, inheritedEnvironment, parseResult.EnvFileOverride);
            AddInfo(messages, $"Workflow [{workflowDocument.Definition.Name}] loaded successfully.");
            AddInfo(messages, $"Workflow version: {workflowDocument.Definition.Version}");
            return true;
        }
        catch (Exception ex)
        {
            workflowDocument = null!;
            AddError(messages, $"Failed to load workflow [{_pathResolver.FormatPath(parseResult.WorkflowPath!)}]: {ex.Message}");
            return false;
        }
    }

    private bool TryLoadVars(
        InlineArguments parseResult,
        WorkflowDocument workflowDocument,
        out bool varsOverrideActive,
        List<CliRunMessage> messages)
    {
        var resolvedVarsFilePath = _pathResolver.ResolveVarsFilePath(parseResult.VarsFilePath, workflowDocument.FilePath, out var varsFileMessage, out var varsFileError);
        if (varsFileError is not null)
        {
            AddError(messages, varsFileError);
            varsOverrideActive = false;
            return false;
        }

        varsOverrideActive = resolvedVarsFilePath is not null;
        if (resolvedVarsFilePath is null)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(varsFileMessage))
        {
            AddInfo(messages, varsFileMessage);
        }

        var varsLoader = _serviceFactory.CreateVarsFileLoader();
        try
        {
            var varsResolution = varsLoader.LoadWithDetails(resolvedVarsFilePath, parseResult.Environment, workflowDocument.Definition.Version);
            parseResult.Inputs.Clear();
            foreach (var pair in varsResolution.Values)
            {
                parseResult.Inputs[pair.Key] = pair.Value;
            }
            if (parseResult.Verbose)
            {
                EmitVarsSources(varsResolution, messages);
            }
            return true;
        }
        catch (Exception ex)
        {
            AddError(messages, $"Failed to load vars file [{_pathResolver.FormatPath(resolvedVarsFilePath)}]: {ex.Message}");
            return false;
        }
    }

    private bool TryValidateWorkflow(
        InlineArguments parseResult,
        WorkflowDocument workflowDocument,
        WorkflowLoader workflowLoader,
        StagePluginRegistry stagePluginRegistry,
        List<CliRunMessage> messages)
    {
        var validator = _serviceFactory.CreateWorkflowValidator(workflowLoader, stagePluginRegistry);
        var validation = validator.ValidateWithDetails(workflowDocument, parseResult.Inputs);
        if (parseResult.DryRun && validation.Warnings.Count > 0)
        {
            AddInfo(messages, $"Dry-run workflow validation warnings: {validation.Warnings.Count}");
            foreach (var warning in validation.Warnings)
            {
                AddInfo(messages, $"Warning: {warning}");
            }
        }

        if (validation.Errors.Count > 0)
        {
            AddError(messages, $"Dry-run failed during workflow validation with {validation.Errors.Count} error(s):");
            foreach (var error in validation.Errors)
            {
                AddError(messages, $"- {error}");
            }
            return false;
        }

        if (parseResult.Verbose)
        {
            AddInfo(messages, $"Workflow loaded: {workflowDocument.Definition.Name} ({workflowDocument.Definition.Id}).");
            AddInfo(messages, $"Init stage variables: {workflowDocument.Definition.InitStage?.Variables?.Count ?? 0}.");
            AddInfo(messages, $"Stages: {workflowDocument.Definition.Stages?.Count ?? 0}.");
        }

        return true;
    }

    private bool TryLoadCatalog(
        string catalogPath,
        WorkflowDocument workflowDocument,
        List<CliRunMessage> messages,
        out IReadOnlyList<ApiCatalogVersion> catalog,
        out ApiCatalogVersion selectedVersion)
    {
        var reader = _serviceFactory.CreateApiCatalogReader();
        try
        {
            catalog = reader.Load(catalogPath);
        }
        catch (Exception ex)
        {
            catalog = Array.Empty<ApiCatalogVersion>();
            selectedVersion = null!;
            AddError(messages, $"Failed to load catalog: [{_pathResolver.FormatPath(catalogPath)}] \n{ex.Message}");
            return false;
        }

        if (catalog.Count == 0)
        {
            selectedVersion = null!;
            AddError(messages, "Catalog does not contain any versions.");
            return false;
        }

        var workflowVersion = workflowDocument.Definition.Version;
        selectedVersion = catalog.FirstOrDefault(item =>
            string.Equals(item.Version, workflowVersion, StringComparison.OrdinalIgnoreCase))
            ?? catalog[0];

        if (!string.Equals(selectedVersion.Version, workflowVersion, StringComparison.OrdinalIgnoreCase))
        {
            AddInfo(messages, $"Warning: workflow version '{workflowVersion}' was not found in catalog. Using catalog version '{selectedVersion.Version}'.");
            AddInfo(messages, $"Available catalog versions: {string.Join(", ", catalog.Select(item => item.Version))}");
        }
        else
        {
            AddInfo(messages, $"Catalog version: {selectedVersion.Version}");
        }

        return true;
    }

    private bool TryValidateEnvironment(
        InlineArguments parseResult,
        WorkflowDocument workflowDocument,
        ApiCatalogVersion selectedVersion,
        List<CliRunMessage> messages)
    {
        var apiEnvironmentErrors = _environmentValidator.Validate(workflowDocument.Definition, selectedVersion, parseResult.Environment!);
        if (apiEnvironmentErrors.Count == 0)
        {
            return true;
        }

        AddError(messages, $"Dry-run failed during environment validation with {apiEnvironmentErrors.Count} error(s):");
        foreach (var error in apiEnvironmentErrors)
        {
            AddError(messages, $"- {error}");
        }
        return false;
    }

    private void EmitBaseUrlInfo(
        IReadOnlyList<ApiDefinition> referencedDefinitions,
        ApiCatalogVersion selectedVersion,
        string environment,
        List<CliRunMessage> messages)
    {
        var connections = selectedVersion.Connections ?? [];
        if (referencedDefinitions.Count == 0 && connections.Count == 0)
        {
            AddInfo(messages, $"Base url: [per-definition, env={environment}]");
            return;
        }

        AddInfo(messages, string.Empty);
        AddInfo(messages, $"API endpoints (env={environment}):");
        foreach (var definition in referencedDefinitions.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
        {
            var baseUrl = CatalogUrlResolver.TryResolveBaseUrl(selectedVersion, definition, environment, out var resolved)
                ? resolved
                : "(unresolved)";
            AddInfo(messages, $"  {definition.Name} -> {baseUrl}");
        }

        foreach (var connection in connections.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
        {
            var baseUrl = connection.BaseUrl is not null &&
                ApiBaseUrlResolver.TryResolveBaseUrl(connection.BaseUrl, environment, out var resolved)
                    ? resolved
                    : "(unresolved)";
            AddInfo(messages, $"  {connection.Name} -> {baseUrl}");
        }
    }

    private async Task<bool> EmitApiHealthChecksAsync(
        IReadOnlyList<ApiDefinition> referencedDefinitions,
        ApiCatalogVersion selectedVersion,
        string environment,
        bool verbose,
        List<CliRunMessage> messages,
        WorkflowPreflightReport preflightReport,
        CancellationToken cancellationToken)
    {
        if (referencedDefinitions.Count == 0)
        {
            return true;
        }

        using var httpClient = _serviceFactory.CreateHttpClient();
        var healthCheckProbe = _serviceFactory.CreateApiHealthCheckProbe();
        var results = await healthCheckProbe.ProbeAsync(httpClient, selectedVersion, referencedDefinitions, environment, cancellationToken);
        if (results.Count == 0)
        {
            return true;
        }

        AddInfo(messages, string.Empty);
        AddInfo(messages, "API health checks:");
        var hasFailures = false;
        foreach (var result in results)
        {
            var definition = selectedVersion.Definitions.First(item =>
                string.Equals(item.Name, result.DefinitionName, StringComparison.OrdinalIgnoreCase));
            var policy = ApiReadinessPolicyResolver.Resolve(definition);
            AddInfo(messages, $"  Policy {result.DefinitionName}: retries={policy.MaxRetries}, delay={policy.DelayMs}ms, timeout={policy.TimeoutMs}ms, healthyStatus={FormatHealthyStatus(policy)}");

            var operation = new WorkflowPreflightOperationRecord
            {
                OperationType = "HealthCheck",
                DefinitionName = result.DefinitionName,
                Target = result.ResolvedUrl ?? result.ConfiguredHealthCheck,
                Status = result.IsHealthy ? "Ok" : "Error",
                Message = result.Message,
                RetryCount = result.RetryCount,
                DurationMs = result.DurationMs
            };
            foreach (var attempt in result.Attempts)
            {
                operation.Attempts.Add(attempt);
            }

            preflightReport.Operations.Add(operation);
            preflightReport.TotalRetries += result.RetryCount;
            var target = result.ResolvedUrl ?? result.ConfiguredHealthCheck ?? "(unresolved)";
            if (verbose)
            {
                EmitHealthCheckAttempts(result, policy, messages);
            }
            if (result.IsHealthy)
            {
                AddInfo(messages, $"  OK {result.DefinitionName} -> {target} after {result.Attempts.Count} attempt(s) in {result.DurationMs} ms.");
                continue;
            }

            hasFailures = true;
            AddError(messages, $"  Failed {result.DefinitionName} -> {target} after {result.Attempts.Count} attempt(s) in {result.DurationMs} ms. {result.Message}");
        }

        preflightReport.DurationMs = preflightReport.Operations.Sum(static operation => operation.DurationMs);
        return !hasFailures;
    }

    private void EmitHealthCheckAttempts(
        ApiHealthCheckResult result,
        ApiReadinessPolicy policy,
        List<CliRunMessage> messages)
    {
        foreach (var attempt in result.Attempts)
        {
            var statusDetail = attempt.HttpStatusCode is int statusCode
                ? $"HTTP {statusCode}"
                : attempt.ErrorMessage ?? "Unknown error";
            AddInfo(messages, $"    attempt {attempt.AttemptNumber}/{policy.MaxRetries + 1}: {statusDetail}");
            if (attempt.AttemptNumber < result.Attempts.Count && policy.DelayMs > 0)
            {
                AddInfo(messages, $"    retrying in {policy.DelayMs}ms...");
            }
        }
    }

    private static string FormatHealthyStatus(ApiReadinessPolicy policy)
        => policy.HttpStatus is null || policy.HttpStatus.Count == 0
            ? "2xx"
            : $"[{string.Join(",", policy.HttpStatus.Order())}]";

    private sealed record ResolvedSecretProviderEnvironment(
        IReadOnlyDictionary<string, string> Secrets,
        IReadOnlyCollection<string> SecretValues)
    {
        public static ResolvedSecretProviderEnvironment Empty { get; } =
            new(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                Array.Empty<string>());
    }
    private async Task<bool> TryCacheSwaggerAndValidateEndpoints(
        InlineArguments parseResult,
        WorkflowDocument workflowDocument,
        ApiCatalogVersion selectedVersion,
        StagePluginRegistry stagePluginRegistry,
        List<CliRunMessage> messages,
        WorkflowPreflightReport preflightReport,
        CancellationToken cancellationToken)
    {
        try
        {
            var cacheRoot = Path.Combine(_pathResolver.ResolveDefaultCacheRoot(parseResult.WorkflowPath), selectedVersion.Version);
            AddInfo(messages, parseResult.RefreshCache
                ? "Swagger definitions: (refreshing from source)"
                : "Swagger definitions: (analyzing cache)");
            using var httpClient = _serviceFactory.CreateHttpClient();
            var cacheService = _serviceFactory.CreateApiSwaggerCacheService(httpClient);
            var swaggerOperations = await cacheService.CacheSwaggerAsync(selectedVersion, parseResult.Environment!, cacheRoot, parseResult.RefreshCache, parseResult.Verbose, cancellationToken);
            foreach (var operation in swaggerOperations)
            {
                preflightReport.Operations.Add(operation);
                preflightReport.TotalRetries += operation.RetryCount;
            }
            preflightReport.DurationMs = preflightReport.Operations.Sum(static operation => operation.DurationMs);

            if (parseResult.Verbose)
            {
                AddInfo(messages, "Validating endpoints against swagger cache...");
            }

            var endpointValidator = _serviceFactory.CreateApiEndpointValidator(stagePluginRegistry);
            var endpointErrors = endpointValidator.Validate(workflowDocument.Definition, selectedVersion, cacheRoot, parseResult.DryRun, parseResult.Verbose && parseResult.DryRun);
            if (endpointErrors.Count > 0)
            {
                AddError(messages, $"Dry-run failed during endpoint validation with {endpointErrors.Count} error(s):");
                foreach (var error in endpointErrors)
                {
                    AddError(messages, $"- {error}");
                }
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            AddError(messages, $"Dry-run failed while caching swagger definitions: {ex.Message}");
            AddInfo(messages, "");
            AddError(messages, "Workflow aborted!");
            return false;
        }
    }

    private CliRunResult BuildDryRunResult(
        WorkflowDocument workflowDocument,
        WorkflowLoader workflowLoader,
        InlineArguments parseResult,
        Stopwatch stopwatch,
        IReadOnlyCollection<string> secretProviderValues,
        List<CliRunMessage> messages,
        int emittedMessageCount)
    {
        if (EmitSelfJumpWarnings(workflowDocument.Definition, messages))
        {
            AddError(messages, "Dry-run failed due to self-jump configuration errors.");
            return new CliRunResult(1, messages, emittedMessageCount);
        }
        var planner = _serviceFactory.CreateWorkflowPlanner(workflowLoader);
        var workflowPlan = planner.BuildPlan(workflowDocument, parseResult.Verbose);
        AddInfo(messages, string.Empty);
        using var writer = new StringWriter();
        _planPrinter.PrintPlan(workflowPlan, 0, parseResult.Verbose, null, null, writer, secretProviderValues);
        AddInfo(messages, writer.ToString().TrimEnd('\r', '\n'));
        AddInfo(messages, string.Empty);
        AddInfo(messages, $"Dry-run completed successfully in {stopwatch.Elapsed.TotalMilliseconds:F0} ms.");
        return new CliRunResult(0, messages, emittedMessageCount);
    }

    private static bool EmitSelfJumpWarnings(WorkflowDefinition definition, List<CliRunMessage> messages)
    {
        if (definition.Stages is null || definition.Stages.Count == 0)
        {
            return false;
        }

        var hasErrors = false;
        foreach (var stage in definition.Stages)
        {
            if (stage.JumpOnStatus is null || stage.JumpOnStatus.Count == 0)
            {
                continue;
            }

            if (stage.Mock is not null)
            {
                var mockStatus = stage.Mock.Status ?? stage.ExpectedStatus ?? 200;
                if (stage.JumpOnStatus.TryGetValue(mockStatus, out var mockTarget) &&
                    string.Equals(mockTarget, stage.Name, StringComparison.OrdinalIgnoreCase))
                {
                    AddError(messages,
                        $"Stage '{stage.Name}' mock status {mockStatus} jumps to itself, creating an infinite loop under --mocked.");
                    hasErrors = true;
                    continue;
                }
            }

            foreach (var target in stage.JumpOnStatus.Values)
            {
                if (string.Equals(target, stage.Name, StringComparison.OrdinalIgnoreCase))
                {
                    AddInfo(messages, $"Warning: stage '{stage.Name}' jumpOnStatus targets itself. Execution will require confirmation if the jump is taken.");
                }
            }
        }

        return hasErrors;
    }

    private async Task<CliRunResult> ExecuteWorkflowAsync(
        InlineArguments parseResult,
        WorkflowDocument workflowDocument,
        ApiCatalogVersion selectedVersion,
        string cacheRoot,
        bool varsOverrideActive,
        WorkflowExecutionReportOptions reportOptions,
        WorkflowPreflightReport preflightReport,
        StagePluginRegistry stagePluginRegistry,
        IReadOnlyCollection<string> secretProviderValues,
        Stopwatch stopwatch,
        List<CliRunMessage> messages,
        int emittedMessageCount,
        CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = _serviceFactory.CreateHttpClient();
            var systemTimeProvider = _serviceFactory.CreateSystemTimeProvider();
            var dynamicValueService = _serviceFactory.CreateDynamicValueService(systemTimeProvider);
            var requestBodyContractProcessor = new RequestBodyContractProcessor(
                RequestContractRegistry.Load(workflowDocument.Definition, selectedVersion, cacheRoot));
            var executor = _serviceFactory.CreateWorkflowExecutor(
                httpClient,
                dynamicValueService,
                systemTimeProvider,
                reportOptions,
                requestBodyContractProcessor,
                stagePluginRegistry,
                secretProviderValues);
            var result = await executor.ExecuteAsync(
                workflowDocument,
                selectedVersion,
                parseResult.Environment!,
                parseResult.Inputs,
                varsOverrideActive,
                parseResult.Mocked,
                parseResult.Verbose,
                parseResult.Debug,
                preflightReport,
                cancellationToken);

            if (result.Output.Count > 0)
            {
                AddInfo(messages, "Output:");
                foreach (var item in result.Output)
                {
                    AddInfo(messages, $"  {item.Key}: {item.Value}");
                }
            }
            else
            {
                AddInfo(messages, "Output: (not defined)");
            }

            if (!string.IsNullOrWhiteSpace(result.OutputFilePath))
            {
                AddInfo(messages, $"Output file: {_pathResolver.FormatPath(result.OutputFilePath)}");
            }

            EmitExecutionArtifactsSummary(result, reportOptions, stopwatch.ElapsedMilliseconds, messages);

            return new CliRunResult(0, messages, emittedMessageCount);
        }
        catch (Exception ex)
        {
            if (ex.Data["workflowExecutionResult"] is WorkflowExecutionResult failedResult)
            {
                EmitExecutionArtifactsSummary(failedResult, reportOptions, stopwatch.ElapsedMilliseconds, messages);
            }

            AddError(messages, $"Workflow [{workflowDocument.Definition.Name}] execution failed: {ex.Message}");
            AddInfo(messages, $"Total execution time: {stopwatch.ElapsedMilliseconds} ms.");
            AddInfo(messages, string.Empty);
            AddError(messages, "Execution aborted!!");
            return new CliRunResult(1, messages, emittedMessageCount);
        }
    }

    private int EmitPendingMessages(IReadOnlyList<CliRunMessage> messages, int alreadyEmittedCount)
    {
        for (var i = alreadyEmittedCount; i < messages.Count; i++)
        {
            var message = messages[i];
            var writer = message.Kind == CliRunMessageKind.Error ? _output.Error : _output.Out;
            var text = message.Kind == CliRunMessageKind.Error
                ? ConsoleMessageFormatter.FormatError(message.Text, _output.UseColors)
                : ConsoleMessageFormatter.FormatInfo(message.Text, _output.UseColors);
            writer.WriteLine(text);
        }

        return messages.Count;
    }

    private void EmitExecutionArtifactsSummary(
        WorkflowExecutionResult result,
        WorkflowExecutionReportOptions reportOptions,
        long totalExecutionMs,
        List<CliRunMessage> messages)
    {
        if (!string.IsNullOrWhiteSpace(result.JsonReportPath))
        {
            AddInfo(messages, $"JSON report: {_pathResolver.FormatPath(result.JsonReportPath)}");
        }

        if (!string.IsNullOrWhiteSpace(result.HtmlReportPath))
        {
            AddInfo(messages, $"HTML report: {_pathResolver.FormatPath(result.HtmlReportPath)}");
        }

        if (!reportOptions.SummaryConsole)
        {
            return;
        }

        AddInfo(messages, string.Empty);
        AddInfo(messages, "Execution summary:");
        AddInfo(messages, $"  executionId: {result.ExecutionId ?? "n/a"}");
        AddInfo(messages, $"  totalDurationMs: {totalExecutionMs}");
        AddInfo(messages, $"  outputFile: {(string.IsNullOrWhiteSpace(result.OutputFilePath) ? "n/a" : _pathResolver.FormatPath(result.OutputFilePath))}");
        AddInfo(messages, $"  jsonReport: {(string.IsNullOrWhiteSpace(result.JsonReportPath) ? "n/a" : _pathResolver.FormatPath(result.JsonReportPath))}");
        AddInfo(messages, $"  htmlReport: {(string.IsNullOrWhiteSpace(result.HtmlReportPath) ? "n/a" : _pathResolver.FormatPath(result.HtmlReportPath))}");
    }

    private static void AddInfo(List<CliRunMessage> messages, string text)
        => messages.Add(new CliRunMessage(CliRunMessageKind.Info, text));

    private static void AddError(List<CliRunMessage> messages, string text)
        => messages.Add(new CliRunMessage(CliRunMessageKind.Error, text));

    private static void EmitVarsSources(VarsFileResolution resolution, List<CliRunMessage> messages)
    {
        if (resolution.Sources.Count == 0)
        {
            AddInfo(messages, "Vars file variable sources: (none)");
            return;
        }

        AddInfo(messages, "Vars file variable sources:");
        foreach (var pair in resolution.Sources.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            AddInfo(messages, $"  {pair.Key}: {FormatVarsSource(pair.Value)}");
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
}
