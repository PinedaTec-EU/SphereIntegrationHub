using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;

using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace SphereIntegrationHub.Tests;

public sealed class HttpEndpointInvokerTests
{
    [Fact]
    public async Task InvokeAsync_SendsRequestAndReturnsResponse()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/api/accounts").UsingPost().WithParam("q", "1"))
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"ok\":true}"));

        var stage = new WorkflowStageDefinition
        {
            Name = "create-account",
            Kind = WorkflowStageKind.Endpoint,
            Endpoint = "/api/accounts",
            HttpVerb = "POST",
            Headers = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json-patch+json",
                ["Authorization"] = "Bearer \"token\""
            },
            Query = new Dictionary<string, string>
            {
                ["q"] = "1"
            },
            Body = "{\"name\":\"test\"}"
        };

        var context = new TemplateContext(
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, string>());

        using var httpClient = new HttpClient();
        var invoker = new HttpEndpointInvoker(httpClient, new TemplateResolver());

        var result = await invoker.InvokeAsync(stage, server.Url!, context, CancellationToken.None);

        Assert.Equal(201, result.Response.StatusCode);
        Assert.Contains("\"ok\":true", result.Response.Body);
        Assert.Equal("POST", result.HttpMethod);
        Assert.Contains("/api/accounts", result.RequestUri);

        var entry = Assert.Single(server.LogEntries);
        var request = entry.RequestMessage!;
        Assert.True(request.Headers!.TryGetValue("Authorization", out var authValues));
        Assert.Contains("Bearer token", authValues);
        Assert.True(request.Headers.TryGetValue("Content-Type", out var contentTypeValues));
        Assert.Contains(contentTypeValues, value =>
            value.StartsWith("application/json-patch+json", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("{\"name\":\"test\"}", request.Body?.ToString());
    }
}
