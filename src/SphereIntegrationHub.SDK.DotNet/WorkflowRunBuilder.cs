using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.cli;
using SphereIntegrationHub.Plugins;
using SphereIntegrationHub.Sdk.Internal;
using SphereIntegrationHub.Services;

namespace SphereIntegrationHub.Sdk;

public sealed class WorkflowRunBuilder
{
    private static readonly TimeSpan HttpRequestTimeout = TimeSpan.FromMinutes(5);

    private readonly string _workflowPath;
    private readonly Dictionary<string, string> _inputs = new(StringComparer.OrdinalIgnoreCase);
    private string? _environment;
    private string? _catalogPath;
    private ApiCatalogVersion? _catalogOverride;
    private string? _envFileOverride;
    private string? _varsFilePath;
    private bool _mocked;
    private bool _verbose;
    private bool _debug;
    private bool _refreshCache;

    internal WorkflowRunBuilder(string workflowPath)
    {
        if (string.IsNullOrWhiteSpace(workflowPath))
        {
            throw new ArgumentException("Workflow path is required.", nameof(workflowPath));
        }

        _workflowPath = Path.GetFullPath(workflowPath);
    }

    public WorkflowRunBuilder Environment(string environment)
    {
        _environment = environment;
        return this;
    }

    public WorkflowRunBuilder Catalog(string catalogPath)
    {
        _catalogPath = Path.GetFullPath(catalogPath);
        _catalogOverride = null;
        return this;
    }

    public WorkflowRunBuilder Catalog(ApiCatalogVersion catalogVersion)
    {
        _catalogOverride = catalogVersion ?? throw new ArgumentNullException(nameof(catalogVersion));
        _catalogPath = null;
        return this;
    }

    public WorkflowRunBuilder EnvFile(string envFilePath)
    {
        _envFileOverride = Path.GetFullPath(envFilePath);
        return this;
    }

    public WorkflowRunBuilder VarsFile(string varsFilePath)
    {
        _varsFilePath = Path.GetFullPath(varsFilePath);
        return this;
    }

    public WorkflowRunBuilder Input(string key, string value)
    {
        _inputs[key] = value;
        return this;
    }

    public WorkflowRunBuilder Inputs(IReadOnlyDictionary<string, string> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        foreach (var pair in inputs)
        {
            _inputs[pair.Key] = pair.Value;
        }

        return this;
    }

    public WorkflowRunBuilder Mocked(bool enabled = true)
    {
        _mocked = enabled;
        return this;
    }

    public WorkflowRunBuilder Verbose(bool enabled = true)
    {
        _verbose = enabled;
        return this;
    }

    public WorkflowRunBuilder Debug(bool enabled = true)
    {
        _debug = enabled;
        return this;
    }

    public WorkflowRunBuilder RefreshCache(bool enabled = true)
    {
        _refreshCache = enabled;
        return this;
    }

    public async Task<WorkflowRunResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_environment))
        {
            throw new InvalidOperationException("Environment is required. Call Environment(...) before ExecuteAsync().");
        }

        var configLoader = new SdkWorkflowConfigLoader();
        var config = configLoader.Load(_workflowPath);
        var logger = new SdkNullExecutionLogger();

        var secretResolution = await ResolveSecretsAsync(config, cancellationToken);
        var workflowLoader = new WorkflowLoader();
        var workflowDocument = workflowLoader.Load(_workflowPath, secretResolution.Secrets, _envFileOverride);

        var inputs = ResolveInputs(workflowDocument);
        var varsFilePath = SdkPathResolver.ResolveVarsFilePath(_varsFilePath, workflowDocument.FilePath);
        if (varsFilePath is not null)
        {
            inputs = LoadVarsInputs(varsFilePath, workflowDocument.Definition.Version);
        }

        foreach (var pair in _inputs)
        {
            inputs[pair.Key] = pair.Value;
        }

        var catalogPath = _catalogOverride is null
            ? _catalogPath ?? SdkPathResolver.ResolveDefaultCatalogPath(workflowDocument.FilePath)
            : null;
        var selectedCatalogVersion = _catalogOverride ?? LoadCatalogVersion(catalogPath!, workflowDocument);
        var stagePluginRegistry = new StagePluginRegistryBuilder().Build(config, selectedCatalogVersion, workflowDocument.FilePath);

        ValidateWorkflow(workflowLoader, workflowDocument, inputs, stagePluginRegistry);
        ValidateEnvironment(workflowDocument, selectedCatalogVersion);

        var preflightReport = new WorkflowPreflightReport();
        var cacheRoot = Path.Combine(SdkPathResolver.ResolveDefaultCacheRoot(workflowDocument.FilePath), selectedCatalogVersion.Version);

        using var httpClient = CreateHttpClient();
        await RunHealthChecksAsync(httpClient, workflowDocument, selectedCatalogVersion, preflightReport, cancellationToken);
        await CacheSwaggerAsync(httpClient, selectedCatalogVersion, cacheRoot, cancellationToken);
        ValidateEndpoints(workflowDocument, selectedCatalogVersion, cacheRoot, stagePluginRegistry, logger);

        var executor = new WorkflowExecutor(
            httpClient,
            new DynamicValueService(),
            requestBodyContractProcessor: null,
            logger: logger,
            reportOptions: ResolveReportOptions(config),
            stagePluginRegistry: stagePluginRegistry,
            preloadedSecretValues: secretResolution.SecretValues);

        var executionResult = await executor.ExecuteAsync(
            workflowDocument,
            selectedCatalogVersion,
            _environment!,
            inputs,
            varsFilePath is not null,
            _mocked,
            _verbose,
            _debug,
            preflightReport,
            cancellationToken);

        return new WorkflowRunResult(
            executionResult.Output,
            workflowDocument.FilePath,
            _environment!,
            selectedCatalogVersion.Version,
            catalogPath,
            varsFilePath,
            executionResult.OutputFilePath,
            executionResult.JsonReportPath,
            executionResult.HtmlReportPath,
            executionResult.ExecutionId);
    }

    private Dictionary<string, string> ResolveInputs(WorkflowDocument workflowDocument)
        => new(_inputs, StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, string> LoadVarsInputs(string varsFilePath, string workflowVersion)
    {
        var varsLoader = new VarsFileLoader();
        return new Dictionary<string, string>(
            varsLoader.Load(varsFilePath, _environment, workflowVersion),
            StringComparer.OrdinalIgnoreCase);
    }

    private static WorkflowExecutionReportOptions ResolveReportOptions(WorkflowConfig config)
    {
        var format = config.Reporting.Format.Trim().ToLowerInvariant() switch
        {
            "none" => ExecutionReportFormat.None,
            "html" => ExecutionReportFormat.Html,
            "both" => ExecutionReportFormat.Both,
            _ => ExecutionReportFormat.Json
        };

        var captureHttp = config.Reporting.CaptureHttp.Trim().ToLowerInvariant() switch
        {
            "none" => ExecutionHttpCaptureMode.None,
            "bodies" => ExecutionHttpCaptureMode.Bodies,
            _ => ExecutionHttpCaptureMode.Headers
        };

        var enabled = config.Reporting.Enabled && format != ExecutionReportFormat.None;
        return new WorkflowExecutionReportOptions(
            enabled,
            format,
            captureHttp,
            config.Reporting.RedactSensitiveData,
            config.Reporting.SummaryConsole);
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            UseCookies = false
        };

        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = HttpRequestTimeout
        };
    }

    private ApiCatalogVersion LoadCatalogVersion(string catalogPath, WorkflowDocument workflowDocument)
    {
        var catalog = new ApiCatalogReader().Load(catalogPath);
        if (catalog.Count == 0)
        {
            throw new InvalidOperationException("Catalog does not contain any versions.");
        }

        var workflowVersion = workflowDocument.Definition.Version;
        return catalog.FirstOrDefault(item =>
                   string.Equals(item.Version, workflowVersion, StringComparison.OrdinalIgnoreCase))
               ?? catalog[0];
    }

    private static void ValidateWorkflow(
        WorkflowLoader workflowLoader,
        WorkflowDocument workflowDocument,
        IReadOnlyDictionary<string, string> inputs,
        StagePluginRegistry stagePluginRegistry)
    {
        var validation = new WorkflowValidator(workflowLoader, stagePluginRegistry).ValidateWithDetails(workflowDocument, inputs);
        if (validation.Errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Workflow validation failed: {string.Join(System.Environment.NewLine, validation.Errors)}");
        }
    }

    private void ValidateEnvironment(WorkflowDocument workflowDocument, ApiCatalogVersion catalogVersion)
    {
        var errors = new List<string>();
        foreach (var definition in catalogVersion.Definitions)
        {
            if (!ApiBaseUrlResolver.TryResolveBaseUrl(catalogVersion, definition, _environment!, out _))
            {
                errors.Add($"Environment '{_environment}' was not found for API definition '{definition.Name}' in catalog version '{catalogVersion.Version}'.");
            }
        }

        foreach (var connection in catalogVersion.Connections ?? Enumerable.Empty<ApiConnectionDefinition>())
        {
            if (connection.BaseUrl is null ||
                !ApiBaseUrlResolver.TryResolveBaseUrl(connection.BaseUrl, _environment!, out _))
            {
                errors.Add($"Environment '{_environment}' was not found for connection '{connection.Name}' in catalog version '{catalogVersion.Version}'.");
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Environment validation failed: {string.Join(System.Environment.NewLine, errors)}");
        }
    }

    private async Task RunHealthChecksAsync(
        HttpClient httpClient,
        WorkflowDocument workflowDocument,
        ApiCatalogVersion catalogVersion,
        WorkflowPreflightReport preflightReport,
        CancellationToken cancellationToken)
    {
        var referencedDefinitions = GetReferencedDefinitions(workflowDocument.Definition, catalogVersion);
        if (referencedDefinitions.Count == 0)
        {
            return;
        }

        var probe = new ApiHealthCheckProbe();
        var results = await probe.ProbeAsync(httpClient, catalogVersion, referencedDefinitions, _environment!, cancellationToken);

        foreach (var result in results)
        {
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
        }

        preflightReport.DurationMs = preflightReport.Operations.Sum(static operation => operation.DurationMs);

        var failedHealthCheck = results.FirstOrDefault(static item => !item.IsHealthy);
        if (failedHealthCheck is not null)
        {
            var target = failedHealthCheck.ResolvedUrl ?? failedHealthCheck.ConfiguredHealthCheck ?? "(unresolved)";
            throw new InvalidOperationException(
                $"Health check failed for '{failedHealthCheck.DefinitionName}' -> {target}: {failedHealthCheck.Message}");
        }
    }

    private async Task CacheSwaggerAsync(
        HttpClient httpClient,
        ApiCatalogVersion catalogVersion,
        string cacheRoot,
        CancellationToken cancellationToken)
    {
        var cacheService = new ApiSwaggerCacheService(httpClient, new SdkNullExecutionLogger());
        await cacheService.CacheSwaggerAsync(catalogVersion, _environment!, cacheRoot, _refreshCache, _verbose, cancellationToken);
    }

    private static void ValidateEndpoints(
        WorkflowDocument workflowDocument,
        ApiCatalogVersion catalogVersion,
        string cacheRoot,
        StagePluginRegistry stagePluginRegistry,
        SdkNullExecutionLogger logger)
    {
        var errors = new ApiEndpointValidator(logger, stagePluginRegistry).Validate(
            workflowDocument.Definition,
            catalogVersion,
            cacheRoot,
            validateRequiredParameters: false,
            verbose: false);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Endpoint validation failed: {string.Join(System.Environment.NewLine, errors)}");
        }
    }

    private async Task<SecretResolution> ResolveSecretsAsync(WorkflowConfig config, CancellationToken cancellationToken)
    {
        if (config.SecretProviders is not { Count: > 0 })
        {
            return SecretResolution.Empty;
        }

        var processEnvironment = System.Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(
                entry => entry.Key.ToString() ?? string.Empty,
                entry => entry.Value?.ToString() ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);

        var resolvedSecrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var secretValues = new HashSet<string>(StringComparer.Ordinal);
        var registry = new SecretProviderRegistryBuilder().Build();
        using var httpClient = CreateHttpClient();

        foreach (var provider in config.SecretProviders)
        {
            if (!registry.TryGet(provider.Plugin, out var plugin))
            {
                throw new InvalidOperationException($"Secret provider plugin '{provider.Plugin}' is not registered.");
            }

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
        }

        return new SecretResolution(resolvedSecrets, secretValues);
    }

    private static List<ApiDefinition> GetReferencedDefinitions(WorkflowDefinition workflow, ApiCatalogVersion catalogVersion)
    {
        if (workflow.References?.Apis is null || workflow.References.Apis.Count == 0)
        {
            return [];
        }

        var definitionNames = workflow.References.Apis
            .Select(static item => item.Definition)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return catalogVersion.Definitions
            .Where(definition => definitionNames.Contains(definition.Name))
            .ToList();
    }

    private sealed record SecretResolution(
        IReadOnlyDictionary<string, string> Secrets,
        IReadOnlyCollection<string> SecretValues)
    {
        public static SecretResolution Empty { get; } =
            new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), Array.Empty<string>());
    }
}
