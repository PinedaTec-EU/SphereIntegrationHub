using SphereIntegrationHub.Services;
using SphereIntegrationHub.Services.Interfaces;
using SphereIntegrationHub.Services.Plugins;

namespace SphereIntegrationHub.cli;

internal sealed class CliServiceFactory : ICliServiceFactory
{
    private readonly IExecutionLogger _logger;

    public CliServiceFactory(ICliOutputProvider? outputProvider = null)
    {
        var output = outputProvider ?? new ConsoleOutputProvider();
        _logger = new ConsoleExecutionLogger(output.Out, output.Error);
    }

    public HttpClient CreateHttpClient() => new();

    public ISystemTimeProvider CreateSystemTimeProvider() => new SystemTimeProvider();

    public DynamicValueService CreateDynamicValueService(ISystemTimeProvider systemTimeProvider)
        => new(systemTimeProvider);

    public WorkflowLoader CreateWorkflowLoader() => new();

    public VarsFileLoader CreateVarsFileLoader() => new();

    public WorkflowValidator CreateWorkflowValidator(
        WorkflowLoader workflowLoader,
        StagePluginRegistry stagePlugins,
        StageValidatorRegistry stageValidators)
        => new(workflowLoader, stagePlugins, stageValidators);

    public ApiCatalogReader CreateApiCatalogReader() => new();

    public ApiSwaggerCacheService CreateApiSwaggerCacheService(HttpClient httpClient)
        => new(httpClient, _logger);

    public ApiEndpointValidator CreateApiEndpointValidator()
        => new(_logger);

    public WorkflowPlanner CreateWorkflowPlanner(WorkflowLoader workflowLoader)
        => new(workflowLoader);

    public WorkflowExecutor CreateWorkflowExecutor(
        HttpClient httpClient,
        DynamicValueService dynamicValueService,
        ISystemTimeProvider systemTimeProvider,
        StagePluginRegistry stagePlugins)
        => new(httpClient, dynamicValueService, stagePlugins, systemProvider: systemTimeProvider, logger: _logger);
}
