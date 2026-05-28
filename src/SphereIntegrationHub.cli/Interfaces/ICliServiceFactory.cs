using SphereIntegrationHub.Services;
using SphereIntegrationHub.Plugins;
using SphereIntegrationHub.Services.Interfaces;

namespace SphereIntegrationHub.cli;

internal interface ICliServiceFactory
{
    HttpClient CreateHttpClient();
    ISystemTimeProvider CreateSystemTimeProvider();
    DynamicValueService CreateDynamicValueService(ISystemTimeProvider systemTimeProvider);
    WorkflowLoader CreateWorkflowLoader();
    VarsFileLoader CreateVarsFileLoader();
    WorkflowValidator CreateWorkflowValidator(WorkflowLoader workflowLoader, StagePluginRegistry? stagePluginRegistry = null);
    ApiCatalogReader CreateApiCatalogReader();
    ApiHealthCheckProbe CreateApiHealthCheckProbe();
    ApiSwaggerCacheService CreateApiSwaggerCacheService(HttpClient httpClient);
    ApiEndpointValidator CreateApiEndpointValidator(StagePluginRegistry? stagePluginRegistry = null);
    WorkflowPlanner CreateWorkflowPlanner(WorkflowLoader workflowLoader);
    WorkflowExecutor CreateWorkflowExecutor(HttpClient httpClient, DynamicValueService dynamicValueService, ISystemTimeProvider systemTimeProvider, WorkflowExecutionReportOptions reportOptions, IRequestBodyContractProcessor? requestBodyContractProcessor = null, StagePluginRegistry? stagePluginRegistry = null, IReadOnlyCollection<string>? preloadedSecretValues = null, bool assertionFailuresBlock = true);
    SecretProviderRegistry CreateSecretProviderRegistry();
}
