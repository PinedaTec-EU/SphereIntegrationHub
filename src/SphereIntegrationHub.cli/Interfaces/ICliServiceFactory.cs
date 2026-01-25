using SphereIntegrationHub.Services;
using SphereIntegrationHub.Services.Interfaces;

namespace SphereIntegrationHub.cli;

internal interface ICliServiceFactory
{
    HttpClient CreateHttpClient();
    ISystemTimeProvider CreateSystemTimeProvider();
    DynamicValueService CreateDynamicValueService(ISystemTimeProvider systemTimeProvider);
    WorkflowLoader CreateWorkflowLoader();
    VarsFileLoader CreateVarsFileLoader();
    WorkflowValidator CreateWorkflowValidator(WorkflowLoader workflowLoader);
    ApiCatalogReader CreateApiCatalogReader();
    ApiSwaggerCacheService CreateApiSwaggerCacheService(HttpClient httpClient);
    ApiEndpointValidator CreateApiEndpointValidator();
    WorkflowPlanner CreateWorkflowPlanner(WorkflowLoader workflowLoader);
    WorkflowExecutor CreateWorkflowExecutor(HttpClient httpClient, DynamicValueService dynamicValueService, ISystemTimeProvider systemTimeProvider);
}
