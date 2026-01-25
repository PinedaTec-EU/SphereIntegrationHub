using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;

namespace SphereIntegrationHub.Services.Interfaces;

public interface IEndpointInvoker
{
    Task<EndpointInvocationResult> InvokeAsync(
        WorkflowStageDefinition stage,
        string baseUrl,
        TemplateContext templateContext,
        CancellationToken cancellationToken);
}

public sealed record EndpointInvocationResult(
    ResponseContext Response,
    string RequestUri,
    string HttpMethod,
    string? RequestBody);
