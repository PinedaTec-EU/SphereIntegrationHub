using System.Net.Http.Headers;

using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.HttpPlugin;

public sealed class HttpStagePlugin : StagePluginBase
{
    public HttpStagePlugin()
        : base("http", WorkflowStageKind.Endpoint, WorkflowStageKind.Http)
    {
    }

    public override void ValidateStage(
        WorkflowStageDefinition stage,
        StagePluginValidationContext context,
        List<string> errors,
        List<string> warnings)
    {
        var apiRef = stage.GetConfigString("apiRef") ?? stage.ApiRef;
        var endpoint = stage.GetConfigString("endpoint") ?? stage.Endpoint;
        var httpVerb = stage.GetConfigString("httpVerb") ?? stage.HttpVerb;
        var body = stage.GetConfigString("body") ?? stage.Body;
        var bodyFile = stage.GetConfigString("bodyFile") ?? stage.BodyFile;

        if (string.IsNullOrWhiteSpace(apiRef))
        {
            errors.Add($"Stage '{stage.Name}' apiRef is required for HTTP stages.");
        }

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            errors.Add($"Stage '{stage.Name}' endpoint is required for HTTP stages.");
        }

        if (string.IsNullOrWhiteSpace(httpVerb))
        {
            errors.Add($"Stage '{stage.Name}' httpVerb is required for HTTP stages.");
        }

        if (!string.IsNullOrWhiteSpace(body) && !string.IsNullOrWhiteSpace(bodyFile))
        {
            errors.Add($"Stage '{stage.Name}' cannot define both body and bodyFile.");
        }
    }

    public override async Task<StagePluginExecutionResult> ExecuteAsync(
        WorkflowStageDefinition stage,
        StagePluginExecutionContext context,
        CancellationToken cancellationToken)
    {
        var effectiveStage = BuildEffectiveStage(stage, context);
        if (string.IsNullOrWhiteSpace(effectiveStage.ApiRef) ||
            !context.ApiBaseUrls.TryGetValue(effectiveStage.ApiRef, out var baseUrl))
        {
            throw new InvalidOperationException($"Stage '{stage.Name}' apiRef '{effectiveStage.ApiRef}' was not found in workflow references.");
        }

        if (context.InvokeEndpointAsync is not null)
        {
            return await context.InvokeEndpointAsync(effectiveStage, baseUrl, cancellationToken);
        }

        var request = BuildTransportRequest(effectiveStage, baseUrl, context);
        var response = await context.SendAsync(request, cancellationToken);
        return new StagePluginExecutionResult(
            new ResponseContext(response.StatusCode, response.Body, response.Headers, TryParseJson(response.Body)),
            response.RequestUri,
            response.Method,
            response.RequestBody);
    }

    private static WorkflowStageDefinition BuildEffectiveStage(WorkflowStageDefinition stage, StagePluginExecutionContext context)
    {
        return new WorkflowStageDefinition
        {
            Name = stage.Name,
            Kind = stage.Kind,
            ApiRef = stage.GetConfigString("apiRef") ?? stage.ApiRef,
            Endpoint = stage.GetConfigString("endpoint") ?? stage.Endpoint,
            HttpVerb = stage.GetConfigString("httpVerb") ?? stage.HttpVerb,
            Headers = stage.GetConfigStringDictionary("headers") ?? stage.Headers,
            Query = stage.GetConfigStringDictionary("query") ?? stage.Query,
            Body = stage.GetConfigString("body") ?? stage.Body,
            BodyFile = stage.GetConfigString("bodyFile") ?? stage.BodyFile,
            Config = stage.Config
        };
    }

    private static StageTransportRequest BuildTransportRequest(
        WorkflowStageDefinition stage,
        string baseUrl,
        StagePluginExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(stage.Endpoint))
        {
            throw new InvalidOperationException($"Stage '{stage.Name}' endpoint is required.");
        }

        if (string.IsNullOrWhiteSpace(stage.HttpVerb))
        {
            throw new InvalidOperationException($"Stage '{stage.Name}' httpVerb is required.");
        }

        var resolvedEndpoint = context.ResolveTemplate(stage.Endpoint);
        var url = BuildRequestUrl(baseUrl, resolvedEndpoint);
        if (stage.Query is not null && stage.Query.Count > 0)
        {
            var queryParts = stage.Query.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(context.ResolveTemplate(pair.Value))}");
            var separator = url.Contains('?', StringComparison.Ordinal) ? '&' : '?';
            url = $"{url}{separator}{string.Join("&", queryParts)}";
        }

        string? contentType = null;
        Dictionary<string, string>? headers = null;
        if (stage.Headers is not null)
        {
            headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in stage.Headers)
            {
                var value = context.ResolveTemplate(header.Value);
                if (string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
                {
                    value = NormalizeAuthorizationHeader(value);
                }

                if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    contentType = MediaTypeHeaderValue.Parse(value).ToString();
                    continue;
                }

                headers[header.Key] = value;
            }
        }

        var bodyTemplate = ResolveBodyTemplate(stage, context);
        var body = string.IsNullOrWhiteSpace(bodyTemplate)
            ? null
            : context.ResolveTemplate(bodyTemplate);

        return new StageTransportRequest(stage.HttpVerb, url, headers, body, contentType ?? "application/json");
    }

    private static string? ResolveBodyTemplate(WorkflowStageDefinition stage, StagePluginExecutionContext context)
    {
        if (!string.IsNullOrWhiteSpace(stage.Body))
        {
            return stage.Body;
        }

        if (string.IsNullOrWhiteSpace(stage.BodyFile))
        {
            return null;
        }

        try
        {
            return context.LoadDataFile(stage.BodyFile);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Stage '{stage.Name}' bodyFile '{stage.BodyFile}' could not be loaded: {ex.Message}",
                ex);
        }
    }

    private static string BuildRequestUrl(string baseUrl, string endpoint)
    {
        var fallback = $"{baseUrl.TrimEnd('/')}/{endpoint.TrimStart('/')}";
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            return fallback;
        }

        var normalizedEndpointPath = NormalizePath(endpoint);
        var normalizedBasePath = NormalizePath(baseUri.AbsolutePath);
        var finalPath = string.IsNullOrEmpty(normalizedBasePath)
            ? normalizedEndpointPath
            : normalizedEndpointPath.StartsWith(normalizedBasePath, StringComparison.OrdinalIgnoreCase)
                ? normalizedEndpointPath
                : $"{normalizedBasePath}{normalizedEndpointPath}";

        var builder = new UriBuilder(baseUri)
        {
            Path = finalPath
        };

        return builder.Uri.ToString().TrimEnd('/');
    }

    private static string NormalizePath(string path)
        => string.IsNullOrWhiteSpace(path) || path == "/" ? string.Empty : "/" + path.Trim('/');

    private static string NormalizeAuthorizationHeader(string value)
    {
        const string bearerPrefix = "Bearer ";
        if (!value.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var token = value[bearerPrefix.Length..].Trim('"', ' ', '\n', '\r');
        return $"{bearerPrefix}{token}";
    }

    private static System.Text.Json.JsonDocument? TryParseJson(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return System.Text.Json.JsonDocument.Parse(body);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
