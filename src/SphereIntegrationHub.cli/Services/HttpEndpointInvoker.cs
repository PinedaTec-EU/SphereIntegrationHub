using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services.Interfaces;

namespace SphereIntegrationHub.Services;

public sealed class HttpEndpointInvoker : IEndpointInvoker
{
    private readonly HttpClient _httpClient;
    private readonly TemplateResolver _templateResolver;

    public HttpEndpointInvoker(HttpClient httpClient, TemplateResolver templateResolver)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _templateResolver = templateResolver ?? throw new ArgumentNullException(nameof(templateResolver));
    }

    public async Task<EndpointInvocationResult> InvokeAsync(
        WorkflowStageDefinition stage,
        string baseUrl,
        TemplateContext templateContext,
        CancellationToken cancellationToken)
    {
        using var activity = Telemetry.ActivitySource.StartActivity(TelemetryConstants.ActivityHttpRequest, ActivityKind.Client);
        activity?.SetTag(TelemetryConstants.TagHttpMethod, stage.HttpVerb);
        activity?.SetTag(TelemetryConstants.TagHttpBaseUrl, baseUrl);
        activity?.SetTag(TelemetryConstants.TagHttpPath, stage.Endpoint);

        var request = BuildRequest(stage, baseUrl, templateContext);
        var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        activity?.SetTag(TelemetryConstants.TagHttpStatusCode, (int)response.StatusCode);
        if ((int)response.StatusCode >= 400)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
        }
        var responseContext = BuildResponseContext(response, body);
        var requestBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        return new EndpointInvocationResult(
            responseContext,
            request.RequestUri?.ToString() ?? string.Empty,
            request.Method.Method,
            requestBody);
    }

    private HttpRequestMessage BuildRequest(WorkflowStageDefinition stage, string baseUrl, TemplateContext context)
    {
        if (string.IsNullOrWhiteSpace(stage.Endpoint))
        {
            throw new InvalidOperationException($"Stage '{stage.Name}' endpoint is required.");
        }

        if (string.IsNullOrWhiteSpace(stage.HttpVerb))
        {
            throw new InvalidOperationException($"Stage '{stage.Name}' httpVerb is required.");
        }

        var url = $"{baseUrl.TrimEnd('/')}/{stage.Endpoint.TrimStart('/')}";
        if (stage.Query is not null && stage.Query.Count > 0)
        {
            var queryParts = stage.Query.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(_templateResolver.ResolveTemplate(pair.Value, context))}");
            url = $"{url}?{string.Join("&", queryParts)}";
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

        if (!string.IsNullOrWhiteSpace(stage.Body))
        {
            var body = _templateResolver.ResolveTemplate(stage.Body, context);
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

    private static ResponseContext BuildResponseContext(HttpResponseMessage response, string body)
    {
        JsonDocument? json = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                json = JsonDocument.Parse(body);
            }
            catch (JsonException)
            {
                json = null;
            }
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers)
        {
            headers[header.Key] = string.Join(",", header.Value);
        }

        foreach (var header in response.Content.Headers)
        {
            headers[header.Key] = string.Join(",", header.Value);
        }

        return new ResponseContext(
            (int)response.StatusCode,
            body,
            headers,
            json);
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
