using System.Net;
using System.Diagnostics;

using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.Services;

public sealed class ApiHealthCheckProbe
{
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

        var tasks = definitions
            .Where(item => !string.IsNullOrWhiteSpace(item.HealthCheck))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(definition => ProbeDefinitionAsync(httpClient, catalogVersion, definition, environment, cancellationToken));

        return await Task.WhenAll(tasks);
    }

    private static async Task<ApiHealthCheckResult> ProbeDefinitionAsync(
        HttpClient httpClient,
        ApiCatalogVersion catalogVersion,
        ApiDefinition definition,
        string environment,
        CancellationToken cancellationToken)
    {
        var operationTimer = Stopwatch.StartNew();
        var attempts = new List<WorkflowPreflightAttemptRecord>();
        Uri healthCheckUri;
        try
        {
            healthCheckUri = CatalogUrlResolver.ResolveHealthCheckUri(catalogVersion, definition, environment);
        }
        catch (Exception ex)
        {
            operationTimer.Stop();
            return new ApiHealthCheckResult(definition.Name, definition.HealthCheck, null, false, null, ex.Message, 0, operationTimer.ElapsedMilliseconds, attempts);
        }

        var policy = ApiReadinessPolicyResolver.Resolve(definition);
        for (var attemptNumber = 1; attemptNumber <= policy.MaxRetries + 1; attemptNumber++)
        {
            var attemptTimer = Stopwatch.StartNew();
            var attempt = new WorkflowPreflightAttemptRecord
            {
                AttemptNumber = attemptNumber,
                RequestUri = healthCheckUri.ToString(),
                StartedAtUtc = DateTimeOffset.UtcNow
            };

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, healthCheckUri);
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(policy.TimeoutMs);
                using var response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutCts.Token);

                var isHealthy = ApiReadinessPolicyResolver.IsHealthyStatus(policy, (int)response.StatusCode);
                attempt.HttpStatusCode = (int)response.StatusCode;
                attempt.Status = isHealthy ? "Ok" : "Error";
                attempt.ErrorMessage = isHealthy ? null : $"Received HTTP {(int)response.StatusCode} ({response.StatusCode}).";

                if (isHealthy)
                {
                    attemptTimer.Stop();
                    attempt.FinishedAtUtc = attempt.StartedAtUtc.AddMilliseconds(attemptTimer.ElapsedMilliseconds);
                    attempt.DurationMs = attemptTimer.ElapsedMilliseconds;
                    attempts.Add(attempt);
                    operationTimer.Stop();
                    return new ApiHealthCheckResult(
                        definition.Name,
                        definition.HealthCheck,
                        healthCheckUri.ToString(),
                        true,
                        response.StatusCode,
                        null,
                        attempts.Count - 1,
                        operationTimer.ElapsedMilliseconds,
                        attempts);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                attempt.Status = "Error";
                attempt.ErrorMessage = $"Health check timed out after {TimeSpan.FromMilliseconds(policy.TimeoutMs).TotalSeconds:F0} s.";
            }
            catch (Exception ex)
            {
                attempt.Status = "Error";
                attempt.ErrorMessage = ex.Message;
            }

            attemptTimer.Stop();
            attempt.FinishedAtUtc = attempt.StartedAtUtc.AddMilliseconds(attemptTimer.ElapsedMilliseconds);
            attempt.DurationMs = attemptTimer.ElapsedMilliseconds;
            attempts.Add(attempt);

            if (attemptNumber <= policy.MaxRetries && policy.DelayMs > 0)
            {
                await Task.Delay(policy.DelayMs, cancellationToken);
            }
        }

        operationTimer.Stop();
        var lastAttempt = attempts[^1];
        return new ApiHealthCheckResult(
            definition.Name,
            definition.HealthCheck,
            healthCheckUri.ToString(),
            false,
            lastAttempt.HttpStatusCode is int statusCode ? (HttpStatusCode)statusCode : null,
            lastAttempt.ErrorMessage,
            attempts.Count - 1,
            operationTimer.ElapsedMilliseconds,
            attempts);
    }
}

public sealed record ApiHealthCheckResult(
    string DefinitionName,
    string? ConfiguredHealthCheck,
    string? ResolvedUrl,
    bool IsHealthy,
    HttpStatusCode? StatusCode,
    string? Message,
    int RetryCount,
    long DurationMs,
    IReadOnlyList<WorkflowPreflightAttemptRecord> Attempts);
