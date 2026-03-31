using System.Net.Http.Headers;
using System.Text;

using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.Services;

internal sealed class HttpRequestBuilder
{
    private readonly TemplateResolver _templateResolver;
    private readonly WorkflowDataFileService _dataFileService;

    public HttpRequestBuilder(TemplateResolver templateResolver)
    {
        _templateResolver = templateResolver;
        _dataFileService = new WorkflowDataFileService();
    }

    public HttpRequestMessage Build(WorkflowStageDefinition stage, string baseUrl, TemplateContext context)
    {
        if (string.IsNullOrWhiteSpace(stage.Endpoint))
        {
            throw new InvalidOperationException($"Stage '{stage.Name}' endpoint is required.");
        }

        if (string.IsNullOrWhiteSpace(stage.HttpVerb))
        {
            throw new InvalidOperationException($"Stage '{stage.Name}' httpVerb is required.");
        }

        var url = BuildRequestUrl(baseUrl, stage.Endpoint);
        if (stage.Query is not null && stage.Query.Count > 0)
        {
            var queryParts = stage.Query.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(_templateResolver.ResolveTemplate(pair.Value, context))}");
            var separator = url.Contains('?', StringComparison.Ordinal) ? '&' : '?';
            url = $"{url}{separator}{string.Join("&", queryParts)}";
        }

        var request = new HttpRequestMessage(new HttpMethod(stage.HttpVerb), url);

        MediaTypeHeaderValue? contentType = null;
        if (stage.Headers is not null)
        {
            foreach (var header in stage.Headers)
            {
                var value = _templateResolver.ResolveTemplate(header.Value, context);
                if (string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
                {
                    value = NormalizeAuthorizationHeader(value);
                }
                if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    contentType = MediaTypeHeaderValue.Parse(value);
                }
                else
                {
                    request.Headers.TryAddWithoutValidation(header.Key, value);
                }
            }
        }

        var bodyTemplate = ResolveBodyTemplate(stage, context);
        if (!string.IsNullOrWhiteSpace(bodyTemplate))
        {
            var body = _templateResolver.ResolveTemplate(bodyTemplate, context);
            var content = new StringContent(body, Encoding.UTF8);
            content.Headers.ContentType = contentType ?? new MediaTypeHeaderValue("application/json");
            request.Content = content;
        }
        else if (contentType is not null)
        {
            request.Content = new StringContent(string.Empty, Encoding.UTF8);
            request.Content.Headers.ContentType = contentType;
        }

        return request;
    }

    private string? ResolveBodyTemplate(WorkflowStageDefinition stage, TemplateContext context)
    {
        if (!string.IsNullOrWhiteSpace(stage.Body) && !string.IsNullOrWhiteSpace(stage.BodyFile))
        {
            throw new InvalidOperationException($"Stage '{stage.Name}' cannot define both body and bodyFile.");
        }

        if (!string.IsNullOrWhiteSpace(stage.Body))
        {
            return stage.Body;
        }

        if (string.IsNullOrWhiteSpace(stage.BodyFile))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(context.WorkflowPath))
        {
            throw new InvalidOperationException($"Stage '{stage.Name}' bodyFile requires workflowPath context.");
        }

        return _dataFileService.LoadText(stage.BodyFile, context.WorkflowPath);
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

        string finalPath;
        if (!string.IsNullOrEmpty(normalizedBasePath) &&
            (normalizedEndpointPath.Equals(normalizedBasePath, StringComparison.OrdinalIgnoreCase) ||
             normalizedEndpointPath.StartsWith($"{normalizedBasePath}/", StringComparison.OrdinalIgnoreCase)))
        {
            finalPath = normalizedEndpointPath;
        }
        else if (string.IsNullOrEmpty(normalizedBasePath))
        {
            finalPath = normalizedEndpointPath;
        }
        else
        {
            finalPath = $"{normalizedBasePath}{normalizedEndpointPath}";
        }

        var builder = new UriBuilder(baseUri)
        {
            Path = finalPath
        };

        return builder.Uri.ToString().TrimEnd('/');
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            return string.Empty;
        }

        return "/" + path.Trim('/');
    }

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
}
