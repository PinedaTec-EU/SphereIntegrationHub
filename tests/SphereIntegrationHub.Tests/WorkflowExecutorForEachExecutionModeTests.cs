using System.Text.Json;

using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;
using SphereIntegrationHub.Services.Interfaces;

namespace SphereIntegrationHub.Tests;

public sealed class WorkflowExecutorForEachExecutionModeTests
{
    [Fact]
    public async Task ExecuteAsync_ForEachWithoutFlag_RunsEndpointIterationsInParallel()
    {
        var invoker = new ConcurrencyTrackingEndpointInvoker(delayMs: 100);
        var result = await ExecuteForEachWorkflowAsync(invoker, forEachSequential: null);

        Assert.True(invoker.MaxConcurrency > 1);
        Assert.Equal("4", result.Output["count"]);
        Assert.Equal("4", result.Output["lastItem"]);
    }

    [Fact]
    public async Task ExecuteAsync_ForEachSequentialTrue_RunsEndpointIterationsSequentially()
    {
        var invoker = new ConcurrencyTrackingEndpointInvoker(delayMs: 50);
        var result = await ExecuteForEachWorkflowAsync(invoker, forEachSequential: true);

        Assert.Equal(1, invoker.MaxConcurrency);
        Assert.Equal("4", result.Output["count"]);
        Assert.Equal("4", result.Output["lastItem"]);
    }

    private static async Task<WorkflowExecutionResult> ExecuteForEachWorkflowAsync(
        IEndpointInvoker invoker,
        bool? forEachSequential)
    {
        using var httpClient = new HttpClient();
        var executor = new WorkflowExecutor(httpClient, new DynamicValueService(), endpointInvoker: invoker);
        var definition = new WorkflowDefinition
        {
            Version = "1.0",
            Id = "parent",
            Name = "parent",
            Output = false,
            References = new WorkflowReference
            {
                Apis = new List<ApiReferenceItem>
                {
                    new() { Name = "accounts", Definition = "accounts" }
                }
            },
            Input = new List<WorkflowInputDefinition>
            {
                new() { Name = "items", Type = RandomValueType.Array, Required = true }
            },
            Stages = new List<WorkflowStageDefinition>
            {
                new()
                {
                    Name = "seed",
                    Kind = WorkflowStageKind.Endpoint,
                    ApiRef = "accounts",
                    Endpoint = "/api/accounts",
                    HttpVerb = "POST",
                    ExpectedStatus = 200,
                    ForEach = "{{input.items}}",
                    ForEachSequential = forEachSequential,
                    Output = new Dictionary<string, string>
                    {
                        ["item"] = "{{context:item}}"
                    }
                }
            },
            EndStage = new WorkflowEndStage
            {
                Output = new Dictionary<string, string>
                {
                    ["count"] = "{{stage:seed.output.foreach_count}}",
                    ["lastItem"] = "{{stage:seed.output.item}}"
                }
            }
        };

        var document = new WorkflowDocument(definition, "/tmp/foreach.workflow", new Dictionary<string, string>());
        return await executor.ExecuteAsync(
            document,
            new ApiCatalogVersion
            {
                Version = "test",
                Definitions = new List<ApiDefinition>
                {
                    new()
                    {
                        Name = "accounts",
                        SwaggerUrl = "http://unused",
                        BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["test"] = "http://example.test"
                        }
                    }
                }
            },
            "test",
            new Dictionary<string, string>
            {
                ["items"] = """["1","2","3","4"]"""
            },
            varsOverrideActive: false,
            mocked: false,
            verbose: false,
            debug: false,
            cancellationToken: CancellationToken.None);
    }

    private sealed class ConcurrencyTrackingEndpointInvoker : IEndpointInvoker
    {
        private readonly int _delayMs;
        private int _currentConcurrency;
        private int _maxConcurrency;

        public ConcurrencyTrackingEndpointInvoker(int delayMs)
        {
            _delayMs = delayMs;
        }

        public int MaxConcurrency => _maxConcurrency;

        public async Task<EndpointInvocationResult> InvokeAsync(
            WorkflowStageDefinition stage,
            string baseUrl,
            TemplateContext templateContext,
            CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref _currentConcurrency);
            UpdateMaxConcurrency(current);

            try
            {
                await Task.Delay(_delayMs, cancellationToken);
                var item = templateContext.Context["item"];
                using var json = JsonDocument.Parse($$"""{"item":"{{item}}"}""");
                return new EndpointInvocationResult(
                    new ResponseContext(
                        200,
                        json.RootElement.GetRawText(),
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                        JsonDocument.Parse(json.RootElement.GetRawText())),
                    $"{baseUrl}/api/accounts",
                    "POST",
                    null);
            }
            finally
            {
                Interlocked.Decrement(ref _currentConcurrency);
            }
        }

        private void UpdateMaxConcurrency(int current)
        {
            while (true)
            {
                var snapshot = _maxConcurrency;
                if (current <= snapshot)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _maxConcurrency, current, snapshot) == snapshot)
                {
                    return;
                }
            }
        }
    }
}
