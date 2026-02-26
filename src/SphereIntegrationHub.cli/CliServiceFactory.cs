using SphereIntegrationHub.Services;
using SphereIntegrationHub.Services.Interfaces;

namespace SphereIntegrationHub.cli;

internal sealed class CliServiceFactory : ICliServiceFactory
{
    private static readonly TimeSpan HttpRequestTimeout = TimeSpan.FromMinutes(5);
    private readonly IExecutionLogger _logger;

    public CliServiceFactory(ICliOutputProvider? outputProvider = null)
    {
        var output = outputProvider ?? new ConsoleOutputProvider();
        _logger = new ConsoleExecutionLogger(output.Out, output.Error);
    }

    public HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            // Avoid CookieContainer initialization issues in restricted runtimes.
            UseCookies = false
        };

        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = HttpRequestTimeout
        };
    }

    public ISystemTimeProvider CreateSystemTimeProvider() => new SystemTimeProvider();

    public DynamicValueService CreateDynamicValueService(ISystemTimeProvider systemTimeProvider)
        => new(systemTimeProvider);

    public WorkflowLoader CreateWorkflowLoader() => new();

    public VarsFileLoader CreateVarsFileLoader() => new();

    public WorkflowValidator CreateWorkflowValidator(WorkflowLoader workflowLoader)
        => new(workflowLoader);

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
        ISystemTimeProvider systemTimeProvider)
        => new(httpClient, dynamicValueService, systemProvider: systemTimeProvider, logger: _logger);
}
