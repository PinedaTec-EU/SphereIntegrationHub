using SphereIntegrationHub.MCP.Models;

namespace SphereIntegrationHub.MCP.Services.Generation;

/// <summary>
/// Generates workflow stage YAML and related artifacts from OpenAPI schema.
/// </summary>
public interface IStageGenerator
{
    Task<string> GenerateEndpointStageAsync(
        string version,
        string apiName,
        string endpoint,
        string httpVerb,
        string? stageName = null,
        EndpointInfo? fallbackEndpoint = null);

    string GenerateWorkflowSkeleton(
        string name,
        string description,
        List<string> inputParameters,
        string version);

    string GenerateWorkflowBundle(
        string name,
        string description,
        string version,
        string apiName,
        IReadOnlyList<Dictionary<string, object?>> stages,
        IReadOnlyCollection<string> inputNames);

    string GenerateWfvars(IReadOnlyCollection<string> inputNames);

    Task<string> GenerateMockPayloadAsync(
        string version,
        string apiName,
        string endpoint,
        string httpVerb,
        EndpointInfo? fallbackEndpoint = null);

    Dictionary<string, object?> BuildEndpointStage(EndpointInfo endpointInfo, string stageName);
}
