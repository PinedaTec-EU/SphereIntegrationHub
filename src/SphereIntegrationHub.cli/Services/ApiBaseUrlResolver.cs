using System;
using System.Collections.Generic;
using System.Linq;

using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.Services;

public static class ApiBaseUrlResolver
{
    public static Dictionary<string, string> BuildApiBaseUrlLookup(
        WorkflowDefinition definition,
        ApiCatalogVersion catalogVersion,
        string environment)
    {
        var apiBaseUrls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (definition.References?.Apis is null || definition.References.Apis.Count == 0)
        {
            return apiBaseUrls;
        }

        foreach (var apiReference in definition.References.Apis)
        {
            var apiDefinition = catalogVersion.Definitions.FirstOrDefault(def =>
                string.Equals(def.Name, apiReference.Definition, StringComparison.OrdinalIgnoreCase));
            if (apiDefinition is null)
            {
                throw new InvalidOperationException(
                    $"API definition '{apiReference.Definition}' was not found in catalog version '{catalogVersion.Version}'.");
            }

            if (!TryResolveBaseUrl(catalogVersion, apiDefinition, environment, out var baseUrl))
            {
                throw new InvalidOperationException(
                    $"Environment '{environment}' was not found for API definition '{apiDefinition.Name}' in catalog version '{catalogVersion.Version}'.");
            }

            apiBaseUrls[apiReference.Name] = CombineBaseUrl(baseUrl!, apiDefinition.BasePath);
        }

        return apiBaseUrls;
    }

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

    private static string CombineBaseUrl(string baseUrl, string? basePath)
    {
        if (string.IsNullOrWhiteSpace(basePath))
        {
            return baseUrl;
        }

        var trimmedBaseUrl = baseUrl.TrimEnd('/');
        var trimmedBasePath = basePath.Trim('/');
        return $"{trimmedBaseUrl}/{trimmedBasePath}";
    }
}
