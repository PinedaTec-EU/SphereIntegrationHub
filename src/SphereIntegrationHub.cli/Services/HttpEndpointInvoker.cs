using System.Diagnostics;
using System.Text.Json;

using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services.Interfaces;

namespace SphereIntegrationHub.Services;

public sealed class HttpEndpointInvoker : IEndpointInvoker
{
    private readonly HttpClient _httpClient;
    private readonly HttpRequestBuilder _requestBuilder;

    public HttpEndpointInvoker(HttpClient httpClient, TemplateResolver templateResolver)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _requestBuilder = new HttpRequestBuilder(templateResolver ?? throw new ArgumentNullException(nameof(templateResolver)));
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

        var request = _requestBuilder.Build(stage, baseUrl, templateContext);
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

}
