using System.Text.Json;

using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;

using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace SphereIntegrationHub.Tests;

public sealed class WorkflowExecutorRequestTests
{
    [Fact]
    public async Task ExecuteAsync_SendsHeadersAndJsonBody()
    {
        using WireMockServer server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/api/accounts").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"ok\":true}"));

        var definition = new WorkflowDefinition
        {
            Version = "3.11",
            Id = "test-workflow",
            Name = "test-workflow",
            References = new WorkflowReference
            {
                Apis = new List<ApiReferenceItem>
                {
                    new()
                    {
                        Name = "accounts",
                        Definition = "accounts"
                    }
                }
            },
            Stages = new List<WorkflowStageDefinition>
            {
                new()
                {
                    Name = "create-account",
                    Kind = WorkflowStageKind.Endpoint,
                    ApiRef = "accounts",
                    Endpoint = "/api/accounts",
                    HttpVerb = "POST",
                    ExpectedStatus = 201,
                    Headers = new Dictionary<string, string>
                    {
                        ["Content-Type"] = "application/json-patch+json",
                        ["Authorization"] = "Bearer token",
                        ["X-Trace"] = "trace-1"
                    },
                    Body = "{\"name\":\"test\"}"
                }
            }
        };

        var document = new WorkflowDocument(
            definition,
            "/tmp/test.workflow",
            new Dictionary<string, string>());

        var catalogVersion = new ApiCatalogVersion
        {
            Version = "test",
            Definitions = new List<ApiDefinition>
            {
                new ApiDefinition { Name = "accounts", SwaggerUrl = "http://unused", BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["test"] = server.Url! } }
            }
        };

        using var httpClient = new HttpClient();
        var executor = new WorkflowExecutor(httpClient, new DynamicValueService());

        await executor.ExecuteAsync(
            document,
            catalogVersion,
            "test",
            new Dictionary<string, string>(),
            varsOverrideActive: false,
            mocked: false,
            verbose: false,
            debug: false,
            cancellationToken: CancellationToken.None);

        var entry = Assert.Single(server.LogEntries);
        var request = entry.RequestMessage!;

        Assert.True(request.Headers!.TryGetValue("Content-Type", out var contentTypeValues));
        Assert.Contains(contentTypeValues, value =>
            value.StartsWith("application/json-patch+json", StringComparison.OrdinalIgnoreCase));
        Assert.True(request.Headers.TryGetValue("X-Trace", out var traceValues));
        Assert.Contains("trace-1", traceValues);
        Assert.True(request.Headers.TryGetValue("Authorization", out var authValues));
        Assert.Contains("Bearer token", authValues);
        Assert.Equal("{\"name\":\"test\"}", request.Body?.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_LoadsBodyFromFileAndIteratesArrayInput()
    {
        using WireMockServer server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/api/accounts").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"ok\":true}"));

        var tempRoot = Path.Combine(Path.GetTempPath(), $"sih-bodyfile-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var workflowPath = Path.Combine(tempRoot, "test.workflow");
        var bodyPath = Path.Combine(tempRoot, "payload.json");
        File.WriteAllText(bodyPath, "{\"id\":\"{{context:item.id}}\"}");

        try
        {
            var definition = new WorkflowDefinition
            {
                Version = "3.11",
                Id = "test-workflow",
                Name = "test-workflow",
                Input = new List<WorkflowInputDefinition>
                {
                    new()
                    {
                        Name = "items",
                        Type = RandomValueType.Array
                    }
                },
                References = new WorkflowReference
                {
                    Apis = new List<ApiReferenceItem>
                    {
                        new()
                        {
                            Name = "accounts",
                            Definition = "accounts"
                        }
                    }
                },
                Stages = new List<WorkflowStageDefinition>
                {
                    new()
                    {
                        Name = "create-account",
                        Kind = WorkflowStageKind.Endpoint,
                        ApiRef = "accounts",
                        Endpoint = "/api/accounts",
                        HttpVerb = "POST",
                        ExpectedStatus = 201,
                        ForEach = "{{input.items}}",
                        BodyFile = "payload.json"
                    }
                }
            };

            var document = new WorkflowDocument(
                definition,
                workflowPath,
                new Dictionary<string, string>());

            var catalogVersion = new ApiCatalogVersion
            {
                Version = "test",
                Definitions = new List<ApiDefinition>
                {
                    new ApiDefinition { Name = "accounts", SwaggerUrl = "http://unused", BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["test"] = server.Url! } }
                }
            };

            using var httpClient = new HttpClient();
            var executor = new WorkflowExecutor(httpClient, new DynamicValueService());

            await executor.ExecuteAsync(
                document,
                catalogVersion,
                "test",
                new Dictionary<string, string>
                {
                    ["items"] = """[{"id":"a-1"},{"id":"a-2"}]"""
                },
                varsOverrideActive: false,
                mocked: false,
                verbose: false,
                debug: false,
                cancellationToken: CancellationToken.None);

            var requests = server.LogEntries.Select(entry => entry.RequestMessage!.Body?.ToString()).ToArray();
            Assert.Equal(2, requests.Length);
            Assert.Contains("{\"id\":\"a-1\"}", requests);
            Assert.Contains("{\"id\":\"a-2\"}", requests);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_InterpolatesEndpointTemplatesFromPreviousStageOutput()
    {
        using WireMockServer server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/api/licensing/tiers").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"tierId\":\"tier-123\"}"));
        server
            .Given(Request.Create().WithPath("/api/licensing/tiers/tier-123/publish").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"published\":true}"));

        var definition = new WorkflowDefinition
        {
            Version = "3.11",
            Id = "test-workflow",
            Name = "test-workflow",
            References = new WorkflowReference
            {
                Apis = new List<ApiReferenceItem>
                {
                    new()
                    {
                        Name = "licensing",
                        Definition = "licensing"
                    }
                }
            },
            Stages = new List<WorkflowStageDefinition>
            {
                new()
                {
                    Name = "create-tier",
                    Kind = WorkflowStageKind.Endpoint,
                    ApiRef = "licensing",
                    Endpoint = "/api/licensing/tiers",
                    HttpVerb = "POST",
                    ExpectedStatus = 201,
                    Output = new Dictionary<string, string>
                    {
                        ["tierId"] = "{{response.body.tierId}}"
                    }
                },
                new()
                {
                    Name = "publish-tier",
                    Kind = WorkflowStageKind.Endpoint,
                    ApiRef = "licensing",
                    Endpoint = "/api/licensing/tiers/{{stage:create-tier.output.tierId}}/publish",
                    HttpVerb = "POST",
                    ExpectedStatus = 200
                }
            }
        };

        var document = new WorkflowDocument(
            definition,
            "/tmp/test.workflow",
            new Dictionary<string, string>());

        var catalogVersion = new ApiCatalogVersion
        {
            Version = "test",
            Definitions = new List<ApiDefinition>
            {
                new ApiDefinition { Name = "licensing", SwaggerUrl = "http://unused", BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["test"] = server.Url! } }
            }
        };

        using var httpClient = new HttpClient();
        var executor = new WorkflowExecutor(httpClient, new DynamicValueService());

        await executor.ExecuteAsync(
            document,
            catalogVersion,
            "test",
            new Dictionary<string, string>(),
            varsOverrideActive: false,
            mocked: false,
            verbose: false,
            debug: false,
            cancellationToken: CancellationToken.None);

        var requests = server.LogEntries.Select(entry => entry.RequestMessage!.Path).ToArray();
        Assert.Equal(2, requests.Length);
        Assert.Contains("/api/licensing/tiers", requests);
        Assert.Contains("/api/licensing/tiers/tier-123/publish", requests);
    }

    [Fact]
    public async Task ExecuteAsync_ConvertsEnumNameUsingRequestContract()
    {
        using WireMockServer server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/api/accounts").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"ok\":true}"));

        var tempRoot = Path.Combine(Path.GetTempPath(), $"sih-contract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var cacheRoot = Path.Combine(tempRoot, "cache");
        Directory.CreateDirectory(cacheRoot);
        File.WriteAllText(
            Path.Combine(cacheRoot, "accounts.json"),
            """
            {
              "openapi": "3.0.1",
              "paths": {
                "/api/accounts": {
                  "post": {
                    "requestBody": {
                      "required": true,
                      "content": {
                        "application/json": {
                          "schema": {
                            "type": "object",
                            "required": ["status"],
                            "properties": {
                              "status": {
                                "type": "integer",
                                "enum": [0, 1],
                                "x-enumNames": ["Pending", "Active"]
                              },
                              "name": {
                                "type": "string"
                              }
                            }
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
            """);

        try
        {
            var definition = CreateRequestContractWorkflow("{\"status\":\"Active\",\"name\":\"test\"}");
            var document = new WorkflowDocument(definition, "/tmp/test.workflow", new Dictionary<string, string>());
            var catalogVersion = CreateRequestContractCatalog(server);
            var processor = new RequestBodyContractProcessor(RequestContractRegistry.Load(definition, catalogVersion, cacheRoot));

            using var httpClient = new HttpClient();
            var executor = new WorkflowExecutor(httpClient, new DynamicValueService(), requestBodyContractProcessor: processor);

            await executor.ExecuteAsync(
                document,
                catalogVersion,
                "test",
                new Dictionary<string, string>(),
                varsOverrideActive: false,
                mocked: false,
                verbose: false,
                debug: false,
                cancellationToken: CancellationToken.None);

            var entry = Assert.Single(server.LogEntries);
            Assert.Equal("{\"status\":1,\"name\":\"test\"}", entry.RequestMessage!.Body?.ToString());
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void ProcessRequestBody_WhenTypeDoesNotMatch_ThrowsFieldFocusedError()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"sih-contract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var cacheRoot = Path.Combine(tempRoot, "cache");
        Directory.CreateDirectory(cacheRoot);
        File.WriteAllText(
            Path.Combine(cacheRoot, "accounts.json"),
            """
            {
              "openapi": "3.0.1",
              "paths": {
                "/api/accounts": {
                  "post": {
                    "requestBody": {
                      "required": true,
                      "content": {
                        "application/json": {
                          "schema": {
                            "type": "object",
                            "required": ["count"],
                            "properties": {
                              "count": {
                                "type": "integer"
                              }
                            }
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
            """);

        try
        {
            var definition = CreateRequestContractWorkflow("{\"count\":\"oops\"}");
            var catalogVersion = CreateRequestContractCatalog(baseUrl: "http://example.test");
            var processor = new RequestBodyContractProcessor(RequestContractRegistry.Load(definition, catalogVersion, cacheRoot));
            var stage = Assert.Single(definition.Stages!);

            var exception = Assert.Throws<InvalidOperationException>(() => processor.Process(stage, "{\"count\":\"oops\"}"));

            Assert.Contains("request body field '$.count'", exception.Message, StringComparison.Ordinal);
            Assert.Contains("expected integer", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("got string", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ClassifiesLatencyUsingWorkflowProfileOverride()
    {
        using WireMockServer server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/api/accounts").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithDelay(TimeSpan.FromMilliseconds(180))
                .WithBody("{\"ok\":true}"));

        var tempRoot = Path.Combine(Path.GetTempPath(), $"sih-latency-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var workflowPath = Path.Combine(tempRoot, "test.workflow");
        File.WriteAllText(workflowPath, "version: 1.0");

        try
        {
            var definition = new WorkflowDefinition
            {
                Version = "3.11",
                Id = "test-workflow",
                Name = "test-workflow",
                References = new WorkflowReference
                {
                    Apis = new List<ApiReferenceItem>
                    {
                        new()
                        {
                            Name = "accounts",
                            Definition = "accounts"
                        }
                    },
                    LatencyProfiles = new List<LatencyProfileDefinition>
                    {
                        new()
                        {
                            Name = "semaphore-default",
                            Bands =
                            [
                                new LatencyBandDefinition { Name = "green", MinMs = 0, MaxMs = 100, Color = "green", Label = "normal" },
                                new LatencyBandDefinition { Name = "amber", MinMs = 101, MaxMs = 250, Color = "amber", Label = "warning" },
                                new LatencyBandDefinition { Name = "red", MinMs = 251, Color = "red", Label = "slow" }
                            ]
                        }
                    }
                },
                Stages = new List<WorkflowStageDefinition>
                {
                    new()
                    {
                        Name = "get-account",
                        Kind = WorkflowStageKind.Endpoint,
                        ApiRef = "accounts",
                        Endpoint = "/api/accounts",
                        HttpVerb = "GET",
                        ExpectedStatus = 200
                    }
                }
            };

            var document = new WorkflowDocument(
                definition,
                workflowPath,
                new Dictionary<string, string>());

            var catalogVersion = new ApiCatalogVersion
            {
                Version = "test",
                LatencyProfiles = new List<LatencyProfileDefinition>
                {
                    new()
                    {
                        Name = "semaphore-default",
                        Bands =
                        [
                            new LatencyBandDefinition { Name = "green", MinMs = 0, MaxMs = 200, Color = "green", Label = "normal" },
                            new LatencyBandDefinition { Name = "amber", MinMs = 201, MaxMs = 500, Color = "amber", Label = "warning" },
                            new LatencyBandDefinition { Name = "red", MinMs = 501, Color = "red", Label = "slow" }
                        ]
                    }
                },
                Definitions = new List<ApiDefinition>
                {
                    new ApiDefinition
                    {
                        Name = "accounts",
                        SwaggerUrl = "http://unused",
                        LatencyProfile = "semaphore-default",
                        BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["test"] = server.Url!
                        }
                    }
                }
            };

            using var httpClient = new HttpClient();
            var executor = new WorkflowExecutor(
                httpClient,
                new DynamicValueService(),
                reportOptions: new WorkflowExecutionReportOptions(true, ExecutionReportFormat.Json, ExecutionHttpCaptureMode.None, true, false));

            var result = await executor.ExecuteAsync(
                document,
                catalogVersion,
                "test",
                new Dictionary<string, string>(),
                varsOverrideActive: false,
                mocked: false,
                verbose: false,
                debug: false,
                cancellationToken: CancellationToken.None);

            Assert.NotNull(result.JsonReportPath);
            using var parsed = JsonDocument.Parse(await File.ReadAllTextAsync(result.JsonReportPath!));
            var latency = parsed.RootElement.GetProperty("Stages")[0].GetProperty("Latency");
            Assert.Equal("amber", latency.GetProperty("Color").GetString());
            Assert.Equal("warning", latency.GetProperty("Label").GetString());
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private static WorkflowDefinition CreateRequestContractWorkflow(string body)
    {
        return new WorkflowDefinition
        {
            Version = "3.11",
            Id = "test-workflow",
            Name = "test-workflow",
            References = new WorkflowReference
            {
                Apis = new List<ApiReferenceItem>
                {
                    new()
                    {
                        Name = "accounts",
                        Definition = "accounts"
                    }
                }
            },
            Stages = new List<WorkflowStageDefinition>
            {
                new()
                {
                    Name = "create-account",
                    Kind = WorkflowStageKind.Endpoint,
                    ApiRef = "accounts",
                    Endpoint = "/api/accounts",
                    HttpVerb = "POST",
                    ExpectedStatus = 201,
                    Body = body
                }
            }
        };
    }

    private static ApiCatalogVersion CreateRequestContractCatalog(WireMockServer server)
        => CreateRequestContractCatalog(server.Url!);

    private static ApiCatalogVersion CreateRequestContractCatalog(string baseUrl)
    {
        return new ApiCatalogVersion
        {
            Version = "test",
            Definitions = new List<ApiDefinition>
            {
                new ApiDefinition { Name = "accounts", SwaggerUrl = "http://unused", BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["test"] = baseUrl } }
            }
        };
    }
}
