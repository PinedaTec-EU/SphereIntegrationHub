using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.Services;

public static class ApiBaseUrlResolver
{
    public static bool TryResolveBaseUrl(
        ApiCatalogVersion catalogVersion,
        ApiDefinition definition,
        string environment,
        out string? baseUrl)
    {
        using var activity = Telemetry.ActivitySource.StartActivity(TelemetryConstants.ActivityApiBaseUrlResolve);
        activity?.SetTag(TelemetryConstants.TagApiDefinition, definition.Name);
        activity?.SetTag(TelemetryConstants.TagEnvironment, environment);

        if (definition.BaseUrl is not null && TryResolveBaseUrlInternal(definition.BaseUrl, environment, out baseUrl))
        {
            return true;
        }

        return TryResolveBaseUrlInternal(catalogVersion.BaseUrl, environment, out baseUrl);
    }

    public static bool TryResolveBaseUrl(
        IReadOnlyDictionary<string, string> baseUrls,
        string environment,
        out string? baseUrl)
    {
        using var activity = Telemetry.ActivitySource.StartActivity(TelemetryConstants.ActivityApiBaseUrlResolve);
        activity?.SetTag(TelemetryConstants.TagEnvironment, environment);

        return TryResolveBaseUrlInternal(baseUrls, environment, out baseUrl);
    }

    private static bool TryResolveBaseUrlInternal(
        IReadOnlyDictionary<string, string> baseUrls,
        string environment,
        out string? baseUrl)
    {
        if (baseUrls.TryGetValue(environment, out baseUrl))
        {
            return true;
        }

        foreach (var pair in baseUrls)
        {
            if (string.Equals(pair.Key, environment, StringComparison.OrdinalIgnoreCase))
            {
                baseUrl = pair.Value;
                return true;
            }
        }

        baseUrl = null;
        return false;
    }
}
