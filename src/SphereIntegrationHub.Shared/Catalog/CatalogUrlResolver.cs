using System.Text.RegularExpressions;

namespace SphereIntegrationHub.Definitions;

public static class CatalogUrlResolver
{
    private static readonly Regex BaseUrlTokenRegex = new(@"\{\{\s*baseUrl(?:\.(?<env>[a-zA-Z0-9_-]+))?\s*\}\}", RegexOptions.Compiled);
    private static readonly Regex PortTokenRegex = new(@"\{\{\s*port\s*\}\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool TryResolveBaseUrl(
        ApiCatalogVersion version,
        ApiDefinition definition,
        string environment,
        out string? baseUrl)
    {
        if (definition.BaseUrl is { Count: > 0 } &&
            TryResolveFromMap(definition.BaseUrl, environment, out baseUrl))
        {
            baseUrl = ApplyPort(baseUrl!, definition.Port);
            return true;
        }

        if (TryResolveFromMap(version.BaseUrl, environment, out baseUrl))
        {
            baseUrl = ApplyPort(baseUrl!, definition.Port);
            return true;
        }

        return false;
    }

    public static Uri ResolveSwaggerUri(ApiCatalogVersion version, ApiDefinition definition, string environment)
    {
        if (!TryResolveBaseUrl(version, definition, environment, out var baseUrl))
        {
            throw new InvalidOperationException(
                $"Cannot resolve swaggerUrl '{definition.SwaggerUrl}' because baseUrl is missing for version '{version.Version}' and definition '{definition.Name}'.");
        }

        var expandedSwaggerUrl = ExpandSwaggerUrlTemplate(definition.SwaggerUrl, version, definition, environment, baseUrl!);
        if (Uri.TryCreate(expandedSwaggerUrl, UriKind.Absolute, out var absolute))
        {
            return absolute;
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException(
                $"Invalid baseUrl '{baseUrl}' for version '{version.Version}'.");
        }

        return new Uri(baseUri, expandedSwaggerUrl.TrimStart('/'));
    }

    private static bool TryResolveFromMap(IReadOnlyDictionary<string, string> map, string environment, out string? baseUrl)
    {
        if (map.TryGetValue(environment, out baseUrl) && !string.IsNullOrWhiteSpace(baseUrl))
        {
            return true;
        }

        foreach (var pair in map)
        {
            if (pair.Key.Equals(environment, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(pair.Value))
            {
                baseUrl = pair.Value;
                return true;
            }
        }

        baseUrl = null;
        return false;
    }

    private static string ExpandSwaggerUrlTemplate(
        string swaggerUrl,
        ApiCatalogVersion version,
        ApiDefinition definition,
        string environment,
        string resolvedBaseUrl)
    {
        var hasExplicitPortToken = PortTokenRegex.IsMatch(swaggerUrl);
        var replacedBaseUrl = BaseUrlTokenRegex.Replace(swaggerUrl, match =>
        {
            var requestedEnvironment = match.Groups["env"].Success
                ? match.Groups["env"].Value
                : environment;

            if (definition.BaseUrl is { Count: > 0 } &&
                TryResolveFromMap(definition.BaseUrl, requestedEnvironment, out var definitionBaseUrl))
            {
                return hasExplicitPortToken
                    ? definitionBaseUrl!
                    : ApplyPort(definitionBaseUrl!, definition.Port);
            }

            if (TryResolveFromMap(version.BaseUrl, requestedEnvironment, out var versionBaseUrl))
            {
                return hasExplicitPortToken
                    ? versionBaseUrl!
                    : ApplyPort(versionBaseUrl!, definition.Port);
            }

            return resolvedBaseUrl;
        });

        if (!hasExplicitPortToken)
        {
            return replacedBaseUrl;
        }

        var explicitPort = definition.Port?.ToString() ?? string.Empty;
        return PortTokenRegex.Replace(replacedBaseUrl, explicitPort);
    }

    private static string ApplyPort(string baseUrl, int? port)
    {
        if (!port.HasValue)
        {
            return baseUrl;
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var absolute))
        {
            return baseUrl;
        }

        var builder = new UriBuilder(absolute)
        {
            Port = port.Value
        };

        return builder.Uri.ToString().TrimEnd('/');
    }
}
