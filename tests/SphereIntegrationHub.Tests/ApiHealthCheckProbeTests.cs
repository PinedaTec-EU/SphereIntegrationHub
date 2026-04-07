using System.Diagnostics;

using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;

using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace SphereIntegrationHub.Tests;

public sealed class ApiHealthCheckProbeTests
{
    private const string Environment = "dev";

    private static ApiCatalogVersion MakeCatalog(params ApiDefinition[] definitions) =>
        new() { Version = "1.0", Definitions = [.. definitions] };

    private static ApiDefinition MakeDefinition(string name, string baseUrl, string? healthCheck = "/health") =>
        new()
        {
            Name = name,
            SwaggerUrl = $"{baseUrl}/swagger.json",
            HealthCheck = healthCheck,
            BaseUrl = new Dictionary<string, string> { [Environment] = baseUrl }
        };

    // -------------------------------------------------------------------------
    // Happy path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProbeAsync_SingleHealthyEndpoint_ReturnsIsHealthyTrue()
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/health").UsingGet())
              .RespondWith(Response.Create().WithStatusCode(200));

        var catalog = MakeCatalog(MakeDefinition("accounts", server.Url!));
        var probe = new ApiHealthCheckProbe();
        using var http = new HttpClient();

        var results = await probe.ProbeAsync(http, catalog, catalog.Definitions, Environment, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.True(result.IsHealthy);
        Assert.Null(result.Message);
        Assert.Equal("accounts", result.DefinitionName);
    }

    [Fact]
    public async Task ProbeAsync_MultipleHealthyEndpoints_AllReturnIsHealthyTrue()
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/health").UsingGet())
              .RespondWith(Response.Create().WithStatusCode(200));

        var catalog = MakeCatalog(
            MakeDefinition("accounts", server.Url!),
            MakeDefinition("billing",  server.Url!),
            MakeDefinition("users",    server.Url!));
        var probe = new ApiHealthCheckProbe();
        using var http = new HttpClient();

        var results = await probe.ProbeAsync(http, catalog, catalog.Definitions, Environment, CancellationToken.None);

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.True(r.IsHealthy));
    }

    // -------------------------------------------------------------------------
    // Unhealthy / mixed
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProbeAsync_EndpointReturnsErrorStatus_ReturnsIsHealthyFalse()
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/health").UsingGet())
              .RespondWith(Response.Create().WithStatusCode(503));

        var catalog = MakeCatalog(MakeDefinition("accounts", server.Url!));
        var probe = new ApiHealthCheckProbe();
        using var http = new HttpClient();

        var results = await probe.ProbeAsync(http, catalog, catalog.Definitions, Environment, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsHealthy);
        Assert.Contains("503", result.Message);
    }

    [Fact]
    public async Task ProbeAsync_MixedEndpoints_EachResultMatchesItsEndpoint()
    {
        using var healthyServer   = WireMockServer.Start();
        using var unhealthyServer = WireMockServer.Start();

        healthyServer.Given(Request.Create().WithPath("/health").UsingGet())
                     .RespondWith(Response.Create().WithStatusCode(200));
        unhealthyServer.Given(Request.Create().WithPath("/health").UsingGet())
                       .RespondWith(Response.Create().WithStatusCode(503));

        var catalog = MakeCatalog(
            MakeDefinition("accounts", healthyServer.Url!),
            MakeDefinition("billing",  unhealthyServer.Url!));
        var probe = new ApiHealthCheckProbe();
        using var http = new HttpClient();

        var results = await probe.ProbeAsync(http, catalog, catalog.Definitions, Environment, CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.True(results.Single(r => r.DefinitionName == "accounts").IsHealthy);
        Assert.False(results.Single(r => r.DefinitionName == "billing").IsHealthy);
    }

    // -------------------------------------------------------------------------
    // Skipping / filtering
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProbeAsync_DefinitionWithoutHealthCheck_IsSkipped()
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/health").UsingGet())
              .RespondWith(Response.Create().WithStatusCode(200));

        var withCheck    = MakeDefinition("accounts", server.Url!, "/health");
        var withoutCheck = MakeDefinition("billing",  server.Url!, healthCheck: null);
        var catalog = MakeCatalog(withCheck, withoutCheck);
        var probe = new ApiHealthCheckProbe();
        using var http = new HttpClient();

        var results = await probe.ProbeAsync(http, catalog, catalog.Definitions, Environment, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal("accounts", result.DefinitionName);
    }

    [Fact]
    public async Task ProbeAsync_NoDefinitionHasHealthCheck_ReturnsEmptyList()
    {
        var catalog = MakeCatalog(
            MakeDefinition("accounts", "http://localhost:9001", healthCheck: null),
            MakeDefinition("billing",  "http://localhost:9002", healthCheck: null));
        var probe = new ApiHealthCheckProbe();
        using var http = new HttpClient();

        var results = await probe.ProbeAsync(http, catalog, catalog.Definitions, Environment, CancellationToken.None);

        Assert.Empty(results);
    }

    // -------------------------------------------------------------------------
    // Ordering
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProbeAsync_ResultsAreOrderedAlphabetically_RegardlessOfInputOrder()
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/health").UsingGet())
              .RespondWith(Response.Create().WithStatusCode(200));

        // Input order: users, accounts, billing
        var catalog = MakeCatalog(
            MakeDefinition("users",    server.Url!),
            MakeDefinition("accounts", server.Url!),
            MakeDefinition("billing",  server.Url!));
        var probe = new ApiHealthCheckProbe();
        using var http = new HttpClient();

        var results = await probe.ProbeAsync(http, catalog, catalog.Definitions, Environment, CancellationToken.None);

        Assert.Equal(["accounts", "billing", "users"], results.Select(r => r.DefinitionName));
    }

    // -------------------------------------------------------------------------
    // Error / unreachable
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProbeAsync_UnreachableEndpoint_ReturnsFailureResultWithMessage()
    {
        var catalog = MakeCatalog(
            MakeDefinition("accounts", "http://localhost:19999")); // nothing listening here
        var probe = new ApiHealthCheckProbe();
        using var http = new HttpClient();

        var results = await probe.ProbeAsync(http, catalog, catalog.Definitions, Environment, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsHealthy);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public async Task ProbeAsync_MissingBaseUrl_ReturnsFailureResultWithMessage()
    {
        var definition = new ApiDefinition
        {
            Name = "accounts",
            SwaggerUrl = "http://example.com/swagger.json",
            HealthCheck = "/health"
            // BaseUrl intentionally omitted
        };
        var catalog = MakeCatalog(definition);
        var probe = new ApiHealthCheckProbe();
        using var http = new HttpClient();

        var results = await probe.ProbeAsync(http, catalog, catalog.Definitions, Environment, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsHealthy);
        Assert.NotNull(result.Message);
    }

    // -------------------------------------------------------------------------
    // Timeout
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProbeAsync_EndpointTimesOut_ReturnsTimeoutMessage()
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/health").UsingGet())
              .RespondWith(Response.Create()
                  .WithStatusCode(200)
                  .WithDelay(TimeSpan.FromSeconds(5))); // exceeds 2s probe timeout

        var catalog = MakeCatalog(MakeDefinition("accounts", server.Url!));
        var probe = new ApiHealthCheckProbe();
        using var http = new HttpClient();

        var results = await probe.ProbeAsync(http, catalog, catalog.Definitions, Environment, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsHealthy);
        Assert.Contains("timed out", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // Parallel execution
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProbeAsync_MultipleEndpoints_RunsInParallel()
    {
        const int endpointCount = 4;
        const int delayMs = 600;

        var servers = Enumerable.Range(0, endpointCount)
            .Select(_ =>
            {
                var s = WireMockServer.Start();
                s.Given(Request.Create().WithPath("/health").UsingGet())
                 .RespondWith(Response.Create()
                     .WithStatusCode(200)
                     .WithDelay(TimeSpan.FromMilliseconds(delayMs)));
                return s;
            })
            .ToList();

        try
        {
            var definitions = servers
                .Select((s, i) => MakeDefinition($"api-{i:D2}", s.Url!))
                .ToList();
            var catalog = MakeCatalog([.. definitions]);
            var probe = new ApiHealthCheckProbe();
            using var http = new HttpClient();

            var sw = Stopwatch.StartNew();
            var results = await probe.ProbeAsync(http, catalog, catalog.Definitions, Environment, CancellationToken.None);
            sw.Stop();

            // All results are healthy
            Assert.Equal(endpointCount, results.Count);
            Assert.All(results, r => Assert.True(r.IsHealthy));

            // Parallel: total time should be well under sequential worst case (endpointCount * delayMs)
            var sequentialMs = endpointCount * delayMs;
            Assert.True(sw.ElapsedMilliseconds < sequentialMs,
                $"Expected parallel execution to finish in < {sequentialMs} ms, but took {sw.ElapsedMilliseconds} ms.");
        }
        finally
        {
            foreach (var s in servers)
            {
                s.Dispose();
            }
        }
    }
}
