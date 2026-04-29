using System.Text.Json;

using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;

using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace SphereIntegrationHub.Tests;

public sealed class OpenAIStagePluginTests
{
    [Fact]
    public async Task ExecuteAsync_CallsResponsesApiAndPublishesTokenUsageOutputs()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/v1/responses").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithHeader("x-request-id", "req-123")
                .WithBody("""
                {
                  "id": "resp_123",
                  "status": "completed",
                  "output_text": "{\"name\":\"Ada\"}",
                  "usage": {
                    "input_tokens": 17,
                    "input_tokens_details": { "cached_tokens": 3 },
                    "output_tokens": 11,
                    "output_tokens_details": { "reasoning_tokens": 5 },
                    "total_tokens": 28
                  }
                }
                """));

        var definition = new WorkflowDefinition
        {
            Version = "1.0",
            Id = "wf-openai-plugin",
            Name = "wf-openai-plugin",
            Input = new List<WorkflowInputDefinition>
            {
                new() { Name = "openai_api_key", Required = true, Secret = true }
            },
            Stages = new List<WorkflowStageDefinition>
            {
                new()
                {
                    Name = "ask",
                    Kind = WorkflowStageKind.Llm,
                    Config = new Dictionary<string, object?>
                    {
                        ["connectionRef"] = "openai-main",
                        ["model"] = "gpt-test",
                        ["inputPrompt"] = "Return a customer named {{input.customer}}.",
                        ["outputPrompt"] = "Return JSON only.",
                        ["reasoning"] = new Dictionary<string, object?>
                        {
                            ["effort"] = "low"
                        },
                        ["limits"] = new Dictionary<string, object?>
                        {
                            ["maxOutputTokens"] = 100,
                            ["maxTotalTokens"] = 200,
                            ["timeoutSeconds"] = 15
                        },
                        ["generation"] = new Dictionary<string, object?>
                        {
                            ["responseFormat"] = "json"
                        }
                    }
                }
            },
            EndStage = new WorkflowEndStage
            {
                Output = new Dictionary<string, string>
                {
                    ["text"] = "{{stage:ask.output.text}}",
                    ["inputTokens"] = "{{stage:ask.output.inputTokens}}",
                    ["outputTokens"] = "{{stage:ask.output.outputTokens}}",
                    ["totalTokens"] = "{{stage:ask.output.totalTokens}}",
                    ["requestId"] = "{{stage:ask.output.requestId}}"
                }
            }
        };

        var document = new WorkflowDocument(definition, "/tmp/openai.workflow", new Dictionary<string, string>());
        var catalogVersion = new ApiCatalogVersion
        {
            Version = "1.0",
            Definitions = new List<ApiDefinition>(),
            Connections = new List<ApiConnectionDefinition>
            {
                new()
                {
                    Name = "openai-main",
                    Type = "llm",
                    Provider = "openai",
                    BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["test"] = $"{server.Url}/v1"
                    },
                    ApiKeySecret = "{{input.openai_api_key}}"
                }
            }
        };

        using var httpClient = new HttpClient();
        var executor = new WorkflowExecutor(httpClient, new DynamicValueService());

        var result = await executor.ExecuteAsync(
            document,
            catalogVersion,
            "test",
            new Dictionary<string, string>
            {
                ["customer"] = "Ada",
                ["openai_api_key"] = "sk-test"
            },
            varsOverrideActive: false,
            mocked: false,
            verbose: false,
            debug: false,
            cancellationToken: CancellationToken.None);

        Assert.Equal("{\"name\":\"Ada\"}", result.Output["text"]);
        Assert.Equal("17", result.Output["inputTokens"]);
        Assert.Equal("11", result.Output["outputTokens"]);
        Assert.Equal("28", result.Output["totalTokens"]);
        Assert.Equal("req-123", result.Output["requestId"]);

        var entry = Assert.Single(server.LogEntries);
        var request = entry.RequestMessage!;
        Assert.True(request.Headers!.TryGetValue("Authorization", out var authValues));
        Assert.Contains("Bearer sk-test", authValues);

        using var body = JsonDocument.Parse(request.Body?.ToString() ?? "{}");
        Assert.Equal("gpt-test", body.RootElement.GetProperty("model").GetString());
        Assert.Equal(100, body.RootElement.GetProperty("max_output_tokens").GetInt32());
        Assert.Equal("low", body.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.Equal("json_object", body.RootElement.GetProperty("text").GetProperty("format").GetProperty("type").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_FailsBeforeRequestWhenEstimatedInputTokensExceedLimit()
    {
        using var server = WireMockServer.Start();
        var definition = new WorkflowDefinition
        {
            Version = "1.0",
            Id = "wf-openai-limit",
            Name = "wf-openai-limit",
            Stages = new List<WorkflowStageDefinition>
            {
                new()
                {
                    Name = "ask",
                    Kind = WorkflowStageKind.Llm,
                    Config = new Dictionary<string, object?>
                    {
                        ["connectionRef"] = "openai-main",
                        ["model"] = "gpt-test",
                        ["apiKey"] = "sk-test",
                        ["inputPrompt"] = "This prompt is intentionally longer than the configured local token limit.",
                        ["limits"] = new Dictionary<string, object?>
                        {
                            ["maxInputTokens"] = 1
                        }
                    }
                }
            }
        };

        var document = new WorkflowDocument(definition, "/tmp/openai-limit.workflow", new Dictionary<string, string>());
        var catalogVersion = new ApiCatalogVersion
        {
            Version = "1.0",
            Definitions = new List<ApiDefinition>(),
            Connections = new List<ApiConnectionDefinition>
            {
                new()
                {
                    Name = "openai-main",
                    Type = "llm",
                    Provider = "openai",
                    BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["test"] = $"{server.Url}/v1"
                    }
                }
            }
        };

        using var httpClient = new HttpClient();
        var executor = new WorkflowExecutor(httpClient, new DynamicValueService());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(
            document,
            catalogVersion,
            "test",
            new Dictionary<string, string>(),
            varsOverrideActive: false,
            mocked: false,
            verbose: false,
            debug: false,
            cancellationToken: CancellationToken.None));

        Assert.Contains("maxInputTokens", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(server.LogEntries);
    }

    [Fact]
    public async Task ExecuteAsync_UsesConnectionConfigDefaults()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/custom/responses").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                {
                  "status": "completed",
                  "output_text": "ok",
                  "usage": {
                    "input_tokens": 1,
                    "output_tokens": 1,
                    "total_tokens": 2
                  }
                }
                """));

        var definition = new WorkflowDefinition
        {
            Version = "1.0",
            Id = "wf-openai-connection-defaults",
            Name = "wf-openai-connection-defaults",
            Stages = new List<WorkflowStageDefinition>
            {
                new()
                {
                    Name = "ask",
                    Kind = WorkflowStageKind.Llm,
                    Config = new Dictionary<string, object?>
                    {
                        ["connectionRef"] = "openai-main",
                        ["inputPrompt"] = "Say ok."
                    }
                }
            },
            EndStage = new WorkflowEndStage
            {
                Output = new Dictionary<string, string>
                {
                    ["text"] = "{{stage:ask.output.text}}"
                }
            }
        };

        var document = new WorkflowDocument(definition, "/tmp/openai-connection-defaults.workflow", new Dictionary<string, string>());
        var catalogVersion = new ApiCatalogVersion
        {
            Version = "1.0",
            Definitions = new List<ApiDefinition>(),
            Connections = new List<ApiConnectionDefinition>
            {
                new()
                {
                    Name = "openai-main",
                    Type = "llm",
                    Provider = "openai",
                    BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["test"] = server.Url!
                    },
                    ApiKeySecret = "sk-test",
                    Config = new Dictionary<string, object?>
                    {
                        ["model"] = "gpt-connection-default",
                        ["endpoint"] = "/custom/responses",
                        ["organization"] = "org-test",
                        ["project"] = "proj-test"
                    }
                }
            }
        };

        using var httpClient = new HttpClient();
        var executor = new WorkflowExecutor(httpClient, new DynamicValueService());

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

        Assert.Equal("ok", result.Output["text"]);

        var entry = Assert.Single(server.LogEntries);
        var request = entry.RequestMessage!;
        using var body = JsonDocument.Parse(request.Body?.ToString() ?? "{}");
        Assert.Equal("gpt-connection-default", body.RootElement.GetProperty("model").GetString());
        Assert.True(request.Headers!.TryGetValue("OpenAI-Organization", out var organizationValues));
        Assert.Contains("org-test", organizationValues);
        Assert.True(request.Headers.TryGetValue("OpenAI-Project", out var projectValues));
        Assert.Contains("proj-test", projectValues);
    }

    [Fact]
    public async Task ExecuteAsync_StillSupportsLegacyApiRefDefinitions()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/v1/responses").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                {
                  "status": "completed",
                  "output_text": "ok",
                  "usage": {
                    "input_tokens": 1,
                    "output_tokens": 1,
                    "total_tokens": 2
                  }
                }
                """));

        var definition = new WorkflowDefinition
        {
            Version = "1.0",
            Id = "wf-openai-legacy",
            Name = "wf-openai-legacy",
            References = new WorkflowReference
            {
                Apis = new List<ApiReferenceItem>
                {
                    new() { Name = "llm", Definition = "openai-main" }
                }
            },
            Stages = new List<WorkflowStageDefinition>
            {
                new()
                {
                    Name = "ask",
                    Kind = WorkflowStageKind.Llm,
                    Config = new Dictionary<string, object?>
                    {
                        ["apiRef"] = "llm",
                        ["model"] = "gpt-test",
                        ["inputPrompt"] = "Say ok."
                    }
                }
            },
            EndStage = new WorkflowEndStage
            {
                Output = new Dictionary<string, string>
                {
                    ["text"] = "{{stage:ask.output.text}}"
                }
            }
        };

        var document = new WorkflowDocument(definition, "/tmp/openai-legacy.workflow", new Dictionary<string, string>());
        var catalogVersion = new ApiCatalogVersion
        {
            Version = "1.0",
            Definitions = new List<ApiDefinition>
            {
                new()
                {
                    Name = "openai-main",
                    ContractType = ApiContractTypes.Llm,
                    BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["test"] = $"{server.Url}/v1"
                    },
                    ApiKeySecret = "sk-test"
                }
            }
        };

        using var httpClient = new HttpClient();
        var executor = new WorkflowExecutor(httpClient, new DynamicValueService());

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

        Assert.Equal("ok", result.Output["text"]);
    }
}
