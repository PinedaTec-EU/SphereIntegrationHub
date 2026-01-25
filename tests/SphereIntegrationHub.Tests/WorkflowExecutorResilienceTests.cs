using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;
using SphereIntegrationHub.Services.Interfaces;

using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace SphereIntegrationHub.Tests;

public sealed class WorkflowExecutorResilienceTests
{
    [Fact]
    public async Task ExecuteAsync_RetryExhaustedOnException_LogsMessage()
    {
        var invoker = new TestEndpointInvoker()
            .EnqueueException(new InvalidOperationException("boom"))
            .EnqueueException(new InvalidOperationException("boom"))
            .EnqueueException(new InvalidOperationException("boom"));
        var logger = new TestExecutionLogger();

        var document = BuildDocument(new WorkflowStageDefinition
        {
            Name = "call",
            Kind = WorkflowStageKind.Endpoint,
            ApiRef = "accounts",
            Endpoint = "/api/accounts",
            HttpVerb = "GET",
            ExpectedStatus = 200,
            Retry = new WorkflowStageRetryDefinition
            {
                MaxRetries = 2,
                DelayMs = 1,
                HttpStatus = [500],
                Messages = new WorkflowStageRetryMessagesDefinition
                {
                    OnException = "Stage failed after retries."
                }
            }
        });

        var catalogVersion = BuildCatalogVersion();
        using var httpClient = new HttpClient();
        var executor = new WorkflowExecutor(
            httpClient,
            new DynamicValueService(),
            endpointInvoker: invoker,
            logger: logger);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(
            document,
            catalogVersion,
            "dev",
            new Dictionary<string, string>(),
            varsOverrideActive: false,
            mocked: false,
            verbose: false,
            debug: false,
            cancellationToken: CancellationToken.None));

        Assert.Contains("failed with exception", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, invoker.InvocationCount);
        Assert.Contains(logger.Errors, message => message.Contains("Stage failed after retries.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_CircuitBreakerOpen_LogsMessagesAndBlocksNextStage()
    {
        var invoker = new TestEndpointInvoker()
            .EnqueueResponse(BuildResponse(500))
            .EnqueueResponse(BuildResponse(500));
        var logger = new TestExecutionLogger();

        var breaker = new WorkflowStageCircuitBreakerDefinition
        {
            Ref = "cb",
            Messages = new WorkflowStageCircuitBreakerMessagesDefinition
            {
                OnOpen = "Breaker opened.",
                OnBlocked = "Breaker open. Skipping."
            }
        };

        var document = BuildDocument(
            new[]
            {
            new WorkflowStageDefinition
            {
                Name = "first",
                Kind = WorkflowStageKind.Endpoint,
                ApiRef = "accounts",
                Endpoint = "/api/accounts/first",
                HttpVerb = "GET",
                ExpectedStatus = 500,
                Retry = new WorkflowStageRetryDefinition
                {
                    MaxRetries = 1,
                    DelayMs = 1,
                    HttpStatus = [ 500 ]
                },
                CircuitBreaker = breaker
            },
            new WorkflowStageDefinition
            {
                Name = "second",
                Kind = WorkflowStageKind.Endpoint,
                ApiRef = "accounts",
                Endpoint = "/api/accounts/second",
                HttpVerb = "GET",
                ExpectedStatus = 200,
                Retry = new WorkflowStageRetryDefinition
                {
                    MaxRetries = 1,
                    DelayMs = 1,
                    HttpStatus = [ 500 ]
                },
                CircuitBreaker = breaker
            }
            },
            new WorkflowResilienceDefinition
            {
                CircuitBreakers = new Dictionary<string, CircuitBreakerDefinition>
                {
                    ["cb"] = new()
                    {
                        FailureThreshold = 1,
                        BreakMs = 60000
                    }
                }
            });

        var catalogVersion = BuildCatalogVersion();
        using var httpClient = new HttpClient();
        var executor = new WorkflowExecutor(
            httpClient,
            new DynamicValueService(),
            endpointInvoker: invoker,
            logger: logger);

        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(
            document,
            catalogVersion,
            "dev",
            new Dictionary<string, string>(),
            varsOverrideActive: false,
            mocked: false,
            verbose: false,
            debug: false,
            cancellationToken: CancellationToken.None));

        Assert.Equal(2, invoker.InvocationCount); // One per stage and one per retry
        Assert.Contains(logger.Infos, message => message.Contains("Breaker opened.", StringComparison.Ordinal));
        Assert.Contains(logger.Infos, message => message.Contains("Breaker open. Skipping.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_RetryMaxHonored_OnHttpStatus()
    {
        using WireMockServer server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/api/accounts").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500));

        var definition = new WorkflowDefinition
        {
            Version = "1.0",
            Id = "wf-1",
            Name = "Test",
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
                    Name = "call",
                    Kind = WorkflowStageKind.Endpoint,
                    ApiRef = "accounts",
                    Endpoint = "/api/accounts",
                    HttpVerb = "GET",
                    ExpectedStatus = 200,
                    Retry = new WorkflowStageRetryDefinition
                    {
                        MaxRetries = 2,
                        DelayMs = 1,
                        HttpStatus = [ 500 ]
                    }
                }
            }
        };

        var document = new WorkflowDocument(
            definition,
            "/tmp/test.workflow",
            new Dictionary<string, string>());

        var catalogVersion = new ApiCatalogVersion(
            "1.0",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["dev"] = server.Url!
            },
            new List<ApiDefinition>
            {
                new("accounts", "http://unused", null, null)
            });

        using var httpClient = new HttpClient();
        var executor = new WorkflowExecutor(httpClient, new DynamicValueService());

        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(
            document,
            catalogVersion,
            "dev",
            new Dictionary<string, string>(),
            varsOverrideActive: false,
            mocked: false,
            verbose: false,
            debug: false,
            cancellationToken: CancellationToken.None));

        Assert.Equal(3, server.LogEntries.Count());
    }

    [Fact]
    public async Task ExecuteAsync_CircuitBreakerBlocksAfterRetries_OnHttpStatus()
    {
        using WireMockServer server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/api/accounts/first").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500));
        server
            .Given(Request.Create().WithPath("/api/accounts/second").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200));

        var definition = new WorkflowDefinition
        {
            Version = "1.0",
            Id = "wf-1",
            Name = "Test",
            Resilience = new WorkflowResilienceDefinition
            {
                CircuitBreakers = new Dictionary<string, CircuitBreakerDefinition>
                {
                    ["cb"] = new()
                    {
                        FailureThreshold = 1,
                        BreakMs = 60000
                    }
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
                    Name = "first",
                    Kind = WorkflowStageKind.Endpoint,
                    ApiRef = "accounts",
                    Endpoint = "/api/accounts/first",
                    HttpVerb = "GET",
                    ExpectedStatus = 500,
                    Retry = new WorkflowStageRetryDefinition
                    {
                        MaxRetries = 1,
                        DelayMs = 1,
                        HttpStatus = [ 500 ]
                    },
                    CircuitBreaker = new WorkflowStageCircuitBreakerDefinition
                    {
                        Ref = "cb"
                    }
                },
                new()
                {
                    Name = "second",
                    Kind = WorkflowStageKind.Endpoint,
                    ApiRef = "accounts",
                    Endpoint = "/api/accounts/second",
                    HttpVerb = "GET",
                    ExpectedStatus = 200,
                    Retry = new WorkflowStageRetryDefinition
                    {
                        MaxRetries = 1,
                        DelayMs = 1,
                        HttpStatus = [ 500 ]
                    },
                    CircuitBreaker = new WorkflowStageCircuitBreakerDefinition
                    {
                        Ref = "cb"
                    }
                }
            }
        };

        var document = new WorkflowDocument(
            definition,
            "/tmp/test.workflow",
            new Dictionary<string, string>());

        var catalogVersion = new ApiCatalogVersion(
            "1.0",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["dev"] = server.Url!
            },
            new List<ApiDefinition>
            {
                new("accounts", "http://unused", null, null)
            });

        using var httpClient = new HttpClient();
        var executor = new WorkflowExecutor(httpClient, new DynamicValueService());

        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(
            document,
            catalogVersion,
            "dev",
            new Dictionary<string, string>(),
            varsOverrideActive: false,
            mocked: false,
            verbose: false,
            debug: false,
            cancellationToken: CancellationToken.None));

        Assert.Equal(2, server.LogEntries.Count());
    }

    private static WorkflowDocument BuildDocument(params WorkflowStageDefinition[] stages)
        => BuildDocument(stages, resilience: null);

    private static WorkflowDocument BuildDocument(
        IReadOnlyList<WorkflowStageDefinition> stages,
        WorkflowResilienceDefinition? resilience)
    {
        var definition = new WorkflowDefinition
        {
            Version = "1.0",
            Id = "wf-1",
            Name = "Test",
            Resilience = resilience,
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
            Stages = stages.ToList()
        };

        return new WorkflowDocument(definition, "/tmp/test.workflow", new Dictionary<string, string>());
    }

    private static ApiCatalogVersion BuildCatalogVersion()
    {
        return new ApiCatalogVersion(
            "1.0",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["dev"] = "http://example.test"
            },
            new List<ApiDefinition>
            {
                new("accounts", "http://unused", null, null)
            });
    }

    private static EndpointInvocationResult BuildResponse(int statusCode)
    {
        return new EndpointInvocationResult(
            new ResponseContext(
                statusCode,
                "{}",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                null),
            "http://example.test/api/accounts",
            "GET",
            null);
    }

    private sealed class TestEndpointInvoker : IEndpointInvoker
    {
        private readonly Queue<object> _actions = new();

        public int InvocationCount { get; private set; }

        public TestEndpointInvoker EnqueueResponse(EndpointInvocationResult response)
        {
            _actions.Enqueue(response);
            return this;
        }

        public TestEndpointInvoker EnqueueException(Exception exception)
        {
            _actions.Enqueue(exception);
            return this;
        }

        public Task<EndpointInvocationResult> InvokeAsync(
            WorkflowStageDefinition stage,
            string baseUrl,
            TemplateContext templateContext,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            var action = _actions.Dequeue();
            if (action is Exception exception)
            {
                throw exception;
            }

            return Task.FromResult((EndpointInvocationResult)action);
        }
    }

    private sealed class TestExecutionLogger : IExecutionLogger
    {
        public List<string> Infos { get; } = new();
        public List<string> Errors { get; } = new();

        public void Info(string message) => Infos.Add(message);

        public void Error(string message) => Errors.Add(message);
    }
}
