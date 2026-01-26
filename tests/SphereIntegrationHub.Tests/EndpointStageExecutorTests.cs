using System.Text.Json;

using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;
using SphereIntegrationHub.Services.Interfaces;
using ExecutionContext = SphereIntegrationHub.Services.ExecutionContext;

namespace SphereIntegrationHub.Tests;

public sealed class EndpointStageExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_StoresOutputsAndReturnsJumpTarget()
    {
        var logger = new TestExecutionLogger();
        var invoker = new TestEndpointInvoker()
            .EnqueueResponse(new ResponseContext(
                200,
                "payload",
                new Dictionary<string, string>(),
                JsonDocument.Parse("{\"id\":1}")));
        var systemProvider = new TestSystemTimeProvider();
        var templateResolver = new TemplateResolver(systemProvider);
        var executor = new EndpointStageExecutor(
            templateResolver,
            new MockPayloadService(),
            systemProvider,
            invoker,
            logger,
            new StageMessageEmitter(templateResolver, logger));

        var definition = new WorkflowDefinition
        {
            Name = "root"
        };
        var stage = new WorkflowStageDefinition
        {
            Name = "create",
            Kind = WorkflowStageKinds.Endpoint,
            ApiRef = "api",
            Endpoint = "/path",
            HttpVerb = "GET",
            ExpectedStatus = 200,
            Message = "ok",
            Output = new Dictionary<string, string>
            {
                ["body"] = "{{response.body}}"
            },
            JumpOnStatus = new Dictionary<int, string>
            {
                [200] = "next"
            }
        };
        var apiBaseUrls = new Dictionary<string, string>
        {
            ["api"] = "http://example.test"
        };
        var context = new ExecutionContext(
            new Dictionary<string, string>(),
            new Dictionary<string, string>());

        var jumpTarget = await executor.ExecuteAsync(
            definition,
            stage,
            apiBaseUrls,
            context,
            verbose: false,
            workflowPath: "/tmp/test.workflow",
            mocked: false,
            cancellationToken: CancellationToken.None);

        Assert.Equal("next", jumpTarget);
        Assert.True(context.EndpointOutputs.TryGetValue("create", out var outputs));
        Assert.Equal("payload", outputs["body"]);
        Assert.Equal("200", outputs["http_status"]);
        Assert.Contains(logger.Infos, message => message.Contains("message: ok", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_RetriesOnExceptionAndEmitsMessage()
    {
        var logger = new TestExecutionLogger();
        var invoker = new TestEndpointInvoker()
            .EnqueueException(new InvalidOperationException("boom"))
            .EnqueueException(new InvalidOperationException("boom"));
        var systemProvider = new TestSystemTimeProvider();
        var templateResolver = new TemplateResolver(systemProvider);
        var executor = new EndpointStageExecutor(
            templateResolver,
            new MockPayloadService(),
            systemProvider,
            invoker,
            logger,
            new StageMessageEmitter(templateResolver, logger));

        var definition = new WorkflowDefinition
        {
            Name = "root",
            Resilience = new WorkflowResilienceDefinition()
        };
        var stage = new WorkflowStageDefinition
        {
            Name = "create",
            Kind = WorkflowStageKinds.Endpoint,
            ApiRef = "api",
            Endpoint = "/path",
            HttpVerb = "GET",
            ExpectedStatus = 200,
            Retry = new WorkflowStageRetryDefinition
            {
                MaxRetries = 1,
                DelayMs = 1,
                HttpStatus = new[] { 500 },
                Messages = new WorkflowStageRetryMessagesDefinition
                {
                    OnException = "retry failed"
                }
            }
        };
        var apiBaseUrls = new Dictionary<string, string>
        {
            ["api"] = "http://example.test"
        };
        var context = new ExecutionContext(
            new Dictionary<string, string>(),
            new Dictionary<string, string>());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(
            definition,
            stage,
            apiBaseUrls,
            context,
            verbose: false,
            workflowPath: "/tmp/test.workflow",
            mocked: false,
            cancellationToken: CancellationToken.None));

        Assert.Contains("failed with exception", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(logger.Errors, message => message.Contains("retry failed", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, invoker.Invocations);
    }

    [Fact]
    public async Task ExecuteAsync_UsesMockPayloadWhenMocked()
    {
        var logger = new TestExecutionLogger();
        var invoker = new TestEndpointInvoker();
        var systemProvider = new TestSystemTimeProvider();
        var templateResolver = new TemplateResolver(systemProvider);
        var executor = new EndpointStageExecutor(
            templateResolver,
            new MockPayloadService(),
            systemProvider,
            invoker,
            logger,
            new StageMessageEmitter(templateResolver, logger));

        var definition = new WorkflowDefinition
        {
            Name = "root"
        };
        var stage = new WorkflowStageDefinition
        {
            Name = "mocked",
            Kind = WorkflowStageKinds.Endpoint,
            ApiRef = "api",
            Endpoint = "/path",
            HttpVerb = "GET",
            ExpectedStatus = 201,
            Mock = new WorkflowStageMockDefinition
            {
                Status = 201,
                Payload = "{\"value\":\"test\"}"
            }
        };
        var apiBaseUrls = new Dictionary<string, string>
        {
            ["api"] = "http://example.test"
        };
        var context = new ExecutionContext(
            new Dictionary<string, string>(),
            new Dictionary<string, string>());

        var jumpTarget = await executor.ExecuteAsync(
            definition,
            stage,
            apiBaseUrls,
            context,
            verbose: false,
            workflowPath: "/tmp/test.workflow",
            mocked: true,
            cancellationToken: CancellationToken.None);

        Assert.Null(jumpTarget);
        Assert.True(context.EndpointOutputs.TryGetValue("mocked", out var outputs));
        Assert.Equal("201", outputs["http_status"]);
        Assert.Equal(0, invoker.Invocations);
    }

    private sealed class TestEndpointInvoker : IEndpointInvoker
    {
        private readonly Queue<Func<EndpointInvocationResult>> _responses = new();

        public int Invocations { get; private set; }

        public TestEndpointInvoker EnqueueResponse(ResponseContext response)
        {
            _responses.Enqueue(() => new EndpointInvocationResult(response, "http://example.test/path", "GET", null));
            return this;
        }

        public TestEndpointInvoker EnqueueException(Exception exception)
        {
            _responses.Enqueue(() => throw exception);
            return this;
        }

        public Task<EndpointInvocationResult> InvokeAsync(
            WorkflowStageDefinition stage,
            string baseUrl,
            TemplateContext templateContext,
            CancellationToken cancellationToken)
        {
            Invocations++;
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No queued responses.");
            }

            return Task.FromResult(_responses.Dequeue().Invoke());
        }
    }

    private sealed class TestExecutionLogger : IExecutionLogger
    {
        public List<string> Infos { get; } = new();
        public List<string> Errors { get; } = new();

        public void Info(string message) => Infos.Add(message);

        public void Error(string message) => Errors.Add(message);
    }

    private sealed class TestSystemTimeProvider : ISystemTimeProvider
    {
        public DateTimeOffset Now { get; } = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        public DateTimeOffset UtcNow => Now;
    }
}
