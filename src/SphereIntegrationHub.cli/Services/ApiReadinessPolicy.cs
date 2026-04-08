using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.Services;

internal sealed record ApiReadinessPolicy(
    int MaxRetries,
    int DelayMs,
    int TimeoutMs,
    IReadOnlySet<int>? HttpStatus);

internal static class ApiReadinessPolicyResolver
{
    private const int DefaultTimeoutMs = 2000;

    public static ApiReadinessPolicy Resolve(ApiDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var readiness = definition.Readiness;
        var maxRetries = readiness?.MaxRetries ?? 0;
        var delayMs = readiness?.DelayMs ?? 0;
        var timeoutMs = readiness?.TimeoutMs ?? DefaultTimeoutMs;
        var httpStatus = readiness?.HttpStatus is { Length: > 0 }
            ? new HashSet<int>(readiness.HttpStatus.Where(static status => status > 0))
            : null;

        if (maxRetries < 0)
        {
            throw new InvalidOperationException($"API definition '{definition.Name}' readiness.maxRetries must be zero or greater.");
        }

        if (delayMs < 0)
        {
            throw new InvalidOperationException($"API definition '{definition.Name}' readiness.delayMs must be zero or greater.");
        }

        if (timeoutMs <= 0)
        {
            throw new InvalidOperationException($"API definition '{definition.Name}' readiness.timeoutMs must be a positive integer.");
        }

        if (readiness?.HttpStatus is { Length: > 0 } && httpStatus?.Count != readiness.HttpStatus.Length)
        {
            throw new InvalidOperationException($"API definition '{definition.Name}' readiness.httpStatus must contain positive integers.");
        }

        return new ApiReadinessPolicy(maxRetries, delayMs, timeoutMs, httpStatus);
    }

    public static bool IsHealthyStatus(ApiReadinessPolicy policy, int statusCode)
        => policy.HttpStatus?.Contains(statusCode) ?? (statusCode >= 200 && statusCode <= 299);
}
