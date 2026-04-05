using SphereIntegrationHub.cli;
using SphereIntegrationHub.Services;
using SphereIntegrationHub.Services.Interfaces;

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

    [Fact]
    public async Task RunAsync_WithShowVersion_Returns0AndPrintsOnlyVersion()
    {
        var output = new TestOutputProvider();
        var usage = new TestUsagePrinter();
        var app = new CliApp(
            argumentParser: new TestParser(new InlineArguments(ShowVersion: true)),
            usagePrinter: usage,
            outputProvider: output,
            serviceFactory: new ThrowingServiceFactory());

        var result = await app.RunAsync(Array.Empty<string>());

        var stdout = ((StringWriter)output.Out).ToString().Trim();
        Assert.Equal(0, result);
        Assert.Equal(0, usage.CallCount);
        Assert.DoesNotContain("Sphere Integration Hub", stdout, StringComparison.Ordinal);
        Assert.Matches(@"^\d+\.\d+\.\d+\.\d+$|^unknown$", stdout);
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
        public bool UseColors => false;
    }

    private sealed class ThrowingServiceFactory : ICliServiceFactory
    {
        public HttpClient CreateHttpClient() => throw new InvalidOperationException("Unexpected factory call");
        public ISystemTimeProvider CreateSystemTimeProvider() => throw new InvalidOperationException("Unexpected factory call");
        public DynamicValueService CreateDynamicValueService(ISystemTimeProvider systemTimeProvider) => throw new InvalidOperationException("Unexpected factory call");
        public WorkflowLoader CreateWorkflowLoader() => throw new InvalidOperationException("Unexpected factory call");
        public VarsFileLoader CreateVarsFileLoader() => throw new InvalidOperationException("Unexpected factory call");
        public WorkflowValidator CreateWorkflowValidator(WorkflowLoader workflowLoader) => throw new InvalidOperationException("Unexpected factory call");
        public ApiCatalogReader CreateApiCatalogReader() => throw new InvalidOperationException("Unexpected factory call");
        public ApiHealthCheckProbe CreateApiHealthCheckProbe() => throw new InvalidOperationException("Unexpected factory call");
        public ApiSwaggerCacheService CreateApiSwaggerCacheService(HttpClient httpClient) => throw new InvalidOperationException("Unexpected factory call");
        public ApiEndpointValidator CreateApiEndpointValidator() => throw new InvalidOperationException("Unexpected factory call");
        public WorkflowPlanner CreateWorkflowPlanner(WorkflowLoader workflowLoader) => throw new InvalidOperationException("Unexpected factory call");
        public WorkflowExecutor CreateWorkflowExecutor(
            HttpClient httpClient,
            DynamicValueService dynamicValueService,
            ISystemTimeProvider systemTimeProvider,
            WorkflowExecutionReportOptions reportOptions,
            IRequestBodyContractProcessor? requestBodyContractProcessor = null)
            => throw new InvalidOperationException("Unexpected factory call");
    }
}
