using SphereIntegrationHub.cli;
using SphereIntegrationHub.Services;
using SphereIntegrationHub.Services.Interfaces;
using SphereIntegrationHub.Services.Plugins;

namespace SphereIntegrationHub.Tests;

public sealed class CliAppTests
{
    [Fact]
    public async Task RunAsync_WithParserError_Returns1AndPrintsUsage()
    {
        var output = new TestOutputProvider();
        var usage = new TestUsagePrinter();
        var app = new CliApp(
            argumentParser: new TestParser(new InlineArguments(Error: "bad")),
            usagePrinter: usage,
            outputProvider: output,
            serviceFactory: new ThrowingServiceFactory());

        var result = await app.RunAsync(Array.Empty<string>());

        Assert.Equal(1, result);
        Assert.Equal(1, usage.CallCount);
        Assert.Contains("bad", output.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_WithShowHelp_Returns0AndPrintsUsage()
    {
        var output = new TestOutputProvider();
        var usage = new TestUsagePrinter();
        var app = new CliApp(
            argumentParser: new TestParser(new InlineArguments(ShowHelp: true)),
            usagePrinter: usage,
            outputProvider: output,
            serviceFactory: new ThrowingServiceFactory());

        var result = await app.RunAsync(Array.Empty<string>());

        Assert.Equal(0, result);
        Assert.Equal(1, usage.CallCount);
    }

    [Fact]
    public async Task RunAsync_MissingRequiredParams_Returns1AndPrintsUsage()
    {
        var output = new TestOutputProvider();
        var usage = new TestUsagePrinter();
        var app = new CliApp(
            argumentParser: new TestParser(new InlineArguments()),
            usagePrinter: usage,
            outputProvider: output,
            serviceFactory: new ThrowingServiceFactory());

        var result = await app.RunAsync(Array.Empty<string>());

        Assert.Equal(1, result);
        Assert.Equal(1, usage.CallCount);
        Assert.Contains("Missing required parameters", output.Error.ToString(), StringComparison.Ordinal);
    }

    private sealed class TestParser : ICliArgumentParser
    {
        private readonly InlineArguments _result;

        public TestParser(InlineArguments result)
        {
            _result = result;
        }

        public InlineArguments ParseArgs(string[] args) => _result;
    }

    private sealed class TestUsagePrinter : ICliUsagePrinter
    {
        public int CallCount { get; private set; }

        public void PrintUsage(TextWriter writer)
        {
            CallCount++;
            writer.WriteLine("usage");
        }
    }

    private sealed class TestOutputProvider : ICliOutputProvider
    {
        public TextWriter Out { get; } = new StringWriter();
        public TextWriter Error { get; } = new StringWriter();
    }

    private sealed class ThrowingServiceFactory : ICliServiceFactory
    {
        public HttpClient CreateHttpClient() => throw new InvalidOperationException("Unexpected factory call");
        public ISystemTimeProvider CreateSystemTimeProvider() => throw new InvalidOperationException("Unexpected factory call");
        public DynamicValueService CreateDynamicValueService(ISystemTimeProvider systemTimeProvider) => throw new InvalidOperationException("Unexpected factory call");
        public WorkflowLoader CreateWorkflowLoader() => throw new InvalidOperationException("Unexpected factory call");
        public VarsFileLoader CreateVarsFileLoader() => throw new InvalidOperationException("Unexpected factory call");
        public WorkflowValidator CreateWorkflowValidator(
            WorkflowLoader workflowLoader,
            StagePluginRegistry stagePlugins,
            StageValidatorRegistry stageValidators)
            => throw new InvalidOperationException("Unexpected factory call");
        public ApiCatalogReader CreateApiCatalogReader() => throw new InvalidOperationException("Unexpected factory call");
        public ApiSwaggerCacheService CreateApiSwaggerCacheService(HttpClient httpClient) => throw new InvalidOperationException("Unexpected factory call");
        public ApiEndpointValidator CreateApiEndpointValidator() => throw new InvalidOperationException("Unexpected factory call");
        public WorkflowPlanner CreateWorkflowPlanner(WorkflowLoader workflowLoader) => throw new InvalidOperationException("Unexpected factory call");
        public WorkflowExecutor CreateWorkflowExecutor(
            HttpClient httpClient,
            DynamicValueService dynamicValueService,
            ISystemTimeProvider systemTimeProvider,
            StagePluginRegistry stagePlugins)
            => throw new InvalidOperationException("Unexpected factory call");
    }
}
