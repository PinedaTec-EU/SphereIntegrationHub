using System.Diagnostics;

using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;

namespace SphereIntegrationHub.cli;

internal sealed class CliPipeline : ICliPipeline
{
    private readonly ICliPathResolver _pathResolver;
    private readonly ICliPlanPrinter _planPrinter;
    private readonly ICliWorkflowEnvironmentValidator _environmentValidator;
    private readonly ICliServiceFactory _serviceFactory;
    private readonly IWorkflowConfigLoader _configLoader;
    private readonly IOpenTelemetryBootstrapper _telemetryBootstrapper;

    public CliPipeline(
        ICliPathResolver pathResolver,
        ICliPlanPrinter planPrinter,
        ICliWorkflowEnvironmentValidator environmentValidator,
        ICliServiceFactory serviceFactory,
        IWorkflowConfigLoader configLoader,
        IOpenTelemetryBootstrapper telemetryBootstrapper)
    {
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _planPrinter = planPrinter ?? throw new ArgumentNullException(nameof(planPrinter));
        _environmentValidator = environmentValidator ?? throw new ArgumentNullException(nameof(environmentValidator));
        _serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));
        _configLoader = configLoader ?? throw new ArgumentNullException(nameof(configLoader));
        _telemetryBootstrapper = telemetryBootstrapper ?? throw new ArgumentNullException(nameof(telemetryBootstrapper));
    }

    public async Task<CliRunResult> RunAsync(InlineArguments parseResult, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var messages = new List<CliRunMessage>();
        var config = _configLoader.Load(parseResult.WorkflowPath);
        if (parseResult.Debug)
        {
            config.OpenTelemetry.DebugConsole = true;
        }
        using var telemetryHandle = _telemetryBootstrapper.Start(config);
        using var activity = Telemetry.ActivitySource.StartActivity(TelemetryConstants.ActivityCliRun);
        activity?.SetTag(TelemetryConstants.TagWorkflowPath, parseResult.WorkflowPath);

        EmitPreamble(parseResult, messages);

        var catalogPath = parseResult.CatalogPath ?? _pathResolver.ResolveDefaultCatalogPath(parseResult.WorkflowPath);
        EmitCatalogInfo(parseResult, catalogPath, messages);

        var workflowLoader = _serviceFactory.CreateWorkflowLoader();
        if (!TryLoadWorkflow(parseResult, workflowLoader, messages, out var workflowDocument))
        {
            return new CliRunResult(1, messages);
        }

        if (!TryLoadVars(parseResult, workflowDocument, out var varsOverrideActive, messages))
        {
            return new CliRunResult(1, messages);
        }

        if (!TryValidateWorkflow(parseResult, workflowDocument, workflowLoader, messages))
        {
            return new CliRunResult(1, messages);
        }

        if (!TryLoadCatalog(catalogPath, workflowDocument, messages, out var catalog, out var selectedVersion))
        {
            return new CliRunResult(1, messages);
        }

        if (!TryValidateEnvironment(parseResult, workflowDocument, selectedVersion, messages))
        {
            return new CliRunResult(1, messages);
        }

        EmitBaseUrlInfo(selectedVersion, parseResult.Environment!, messages);

        if (!await TryCacheSwaggerAndValidateEndpoints(parseResult, workflowDocument, selectedVersion, messages, cancellationToken))
        {
            return new CliRunResult(1, messages);
        }

        if (parseResult.DryRun)
        {
            return BuildDryRunResult(workflowDocument, workflowLoader, parseResult, stopwatch, messages);
        }

        return await ExecuteWorkflowAsync(parseResult, workflowDocument, selectedVersion, varsOverrideActive, messages, cancellationToken);
    }

    private void EmitPreamble(InlineArguments parseResult, List<CliRunMessage> messages)
    {
        if (parseResult.DryRun)
        {
            AddInfo(messages, "Starting dry-run...");
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

    private bool TryLoadWorkflow(
        InlineArguments parseResult,
        WorkflowLoader workflowLoader,
        List<CliRunMessage> messages,
        out WorkflowDocument workflowDocument)
    {
        try
        {
            workflowDocument = workflowLoader.Load(parseResult.WorkflowPath!, null, parseResult.EnvFileOverride);
            AddInfo(messages, $"Workflow [{workflowDocument.Definition.Name}] loaded successfully.");
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
        List<CliRunMessage> messages)
    {
        var validator = _serviceFactory.CreateWorkflowValidator(workflowLoader);
        var validationErrors = validator.Validate(workflowDocument);
        if (validationErrors.Count > 0)
        {
            AddError(messages, "Workflow validation failed:");
            foreach (var error in validationErrors)
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

        var workflowVersion = workflowDocument.Definition.Version;
        selectedVersion = catalog.FirstOrDefault(item =>
            string.Equals(item.Version, workflowVersion, StringComparison.OrdinalIgnoreCase))!;

        if (selectedVersion is null)
        {
            AddError(messages, $"Catalog version '{workflowVersion}' was not found.");
            AddError(messages, $"Available versions: {string.Join(", ", catalog.Select(item => item.Version))}");
            return false;
        }

        AddInfo(messages, $"Version: {selectedVersion.Version}");
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

        AddError(messages, "Environment validation failed:");
        foreach (var error in apiEnvironmentErrors)
        {
            AddError(messages, $"- {error}");
        }
        return false;
    }

    private void EmitBaseUrlInfo(ApiCatalogVersion selectedVersion, string environment, List<CliRunMessage> messages)
    {
        if (ApiBaseUrlResolver.TryResolveBaseUrl(selectedVersion.BaseUrl, environment, out var defaultBaseUrl))
        {
            AddInfo(messages, $"Base url: {defaultBaseUrl}");
        }
        else
        {
            AddInfo(messages, "Base url: [per-definition overrides]");
        }
    }

    private async Task<bool> TryCacheSwaggerAndValidateEndpoints(
        InlineArguments parseResult,
        WorkflowDocument workflowDocument,
        ApiCatalogVersion selectedVersion,
        List<CliRunMessage> messages,
        CancellationToken cancellationToken)
    {
        try
        {
            var cacheRoot = Path.Combine(_pathResolver.ResolveDefaultCacheRoot(parseResult.WorkflowPath), selectedVersion.Version);
            using var httpClient = _serviceFactory.CreateHttpClient();
            var cacheService = _serviceFactory.CreateApiSwaggerCacheService(httpClient);
            await cacheService.CacheSwaggerAsync(selectedVersion, workflowDocument.Definition, parseResult.Environment!, cacheRoot, parseResult.RefreshCache, parseResult.Verbose, cancellationToken);

            if (parseResult.Verbose)
            {
                AddInfo(messages, "Validating endpoints against swagger cache...");
            }

            var endpointValidator = _serviceFactory.CreateApiEndpointValidator();
            var endpointErrors = endpointValidator.Validate(workflowDocument.Definition, selectedVersion, cacheRoot, parseResult.DryRun, parseResult.Verbose && parseResult.DryRun);
            if (endpointErrors.Count > 0)
            {
                AddError(messages, "Endpoint validation failed:");
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
            AddError(messages, $"Failed to cache swagger definitions: {ex.Message}");
            return false;
        }
    }

    private CliRunResult BuildDryRunResult(
        WorkflowDocument workflowDocument,
        WorkflowLoader workflowLoader,
        InlineArguments parseResult,
        Stopwatch stopwatch,
        List<CliRunMessage> messages)
    {
        if (EmitSelfJumpWarnings(workflowDocument.Definition, messages))
        {
            AddError(messages, "Dry-run failed due to self-jump configuration errors.");
            return new CliRunResult(1, messages);
        }
        var planner = _serviceFactory.CreateWorkflowPlanner(workflowLoader);
        var workflowPlan = planner.BuildPlan(workflowDocument, parseResult.Verbose);
        AddInfo(messages, string.Empty);
        using var writer = new StringWriter();
        _planPrinter.PrintPlan(workflowPlan, 0, parseResult.Verbose, null, null, writer);
        AddInfo(messages, writer.ToString().TrimEnd('\r', '\n'));
        AddInfo(messages, string.Empty);
        AddInfo(messages, $"Dry-run completed successfully in {stopwatch.Elapsed.TotalMilliseconds:F0} ms.");
        return new CliRunResult(0, messages);
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
        bool varsOverrideActive,
        List<CliRunMessage> messages,
        CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = _serviceFactory.CreateHttpClient();
            var systemTimeProvider = _serviceFactory.CreateSystemTimeProvider();
            var dynamicValueService = _serviceFactory.CreateDynamicValueService(systemTimeProvider);
            var executor = _serviceFactory.CreateWorkflowExecutor(httpClient, dynamicValueService, systemTimeProvider);
            var result = await executor.ExecuteAsync(
                workflowDocument,
                selectedVersion,
                parseResult.Environment!,
                parseResult.Inputs,
                varsOverrideActive,
                parseResult.Mocked,
                parseResult.Verbose,
                parseResult.Debug,
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

            return new CliRunResult(0, messages);
        }
        catch (Exception ex)
        {
            AddError(messages, $"Workflow [{workflowDocument.Definition.Name}] execution failed: {ex.Message}");
            AddError(messages, "Execution aborted.");
            return new CliRunResult(1, messages);
        }
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
