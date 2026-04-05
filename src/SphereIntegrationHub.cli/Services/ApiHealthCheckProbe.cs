using System.Net;

using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.Services;

public sealed class ApiHealthCheckProbe
{
    private static readonly TimeSpan HealthCheckTimeout = TimeSpan.FromSeconds(2);

    public async Task<IReadOnlyList<ApiHealthCheckResult>> ProbeAsync(
        HttpClient httpClient,
        ApiCatalogVersion catalogVersion,
        IEnumerable<ApiDefinition> definitions,
        string environment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(catalogVersion);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentException.ThrowIfNullOrWhiteSpace(environment);

        var results = new List<ApiHealthCheckResult>();
        foreach (var definition in definitions
                     .Where(item => !string.IsNullOrWhiteSpace(item.HealthCheck))
                     .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            results.Add(await ProbeDefinitionAsync(httpClient, catalogVersion, definition, environment, cancellationToken));
        }

        return results;
    }

    private static async Task<ApiHealthCheckResult> ProbeDefinitionAsync(
        HttpClient httpClient,
        ApiCatalogVersion catalogVersion,
        ApiDefinition definition,
        string environment,
        CancellationToken cancellationToken)
    {
        Uri healthCheckUri;
        try
        {
            healthCheckUri = CatalogUrlResolver.ResolveHealthCheckUri(catalogVersion, definition, environment);
        }
        catch (Exception ex)
        {
            return new ApiHealthCheckResult(definition.Name, definition.HealthCheck, null, false, null, ex.Message);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, healthCheckUri);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(HealthCheckTimeout);
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token);

            var isHealthy = response.IsSuccessStatusCode;
            var message = isHealthy
                ? null
                : $"Received HTTP {(int)response.StatusCode} ({response.StatusCode}).";

            return new ApiHealthCheckResult(
                definition.Name,
                definition.HealthCheck,
                healthCheckUri.ToString(),
                isHealthy,
                response.StatusCode,
                message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ApiHealthCheckResult(
                definition.Name,
                definition.HealthCheck,
                healthCheckUri.ToString(),
                false,
                null,
                $"Health check timed out after {HealthCheckTimeout.TotalSeconds:F0} s.");
        }
        catch (Exception ex)
        {
            return new ApiHealthCheckResult(
                definition.Name,
                definition.HealthCheck,
                healthCheckUri.ToString(),
                false,
                null,
                ex.Message);
        }
    }
}

public sealed record ApiHealthCheckResult(
    string DefinitionName,
    string? ConfiguredHealthCheck,
    string? ResolvedUrl,
    bool IsHealthy,
    HttpStatusCode? StatusCode,
    string? Message);
