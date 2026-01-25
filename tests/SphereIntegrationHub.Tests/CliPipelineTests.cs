using SphereIntegrationHub.cli;
using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.Tests;

public sealed class CliPipelineTests
{
    [Fact]
    public async Task RunAsync_DryRun_SucceedsAndPrintsPlan()
    {
        var fixture = CreateFixture(swaggerHasEndpoint: true, includeMock: true, catalogVersion: "1.0", baseUrls: new Dictionary<string, string> { ["dev"] = "http://example.test" });
        var pipeline = CreatePipeline();

        var result = await pipeline.RunAsync(new InlineArguments(
            WorkflowPath: fixture.WorkflowPath,
            Environment: "dev",
            CatalogPath: null,
            DryRun: true,
            Mocked: false), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.Messages, message => message.Text.Contains("Dry-run completed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Messages, message => message.Text.Contains("Workflow:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_WorkflowMissing_ReturnsError()
    {
        var pipeline = CreatePipeline();
        var missingPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.workflow");

        var result = await pipeline.RunAsync(new InlineArguments(
            WorkflowPath: missingPath,
            Environment: "dev",
            CatalogPath: "/tmp/unused.json"), CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(result.Messages, message => message.Text.Contains("Failed to load workflow", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_CatalogVersionNotFound_ReturnsError()
    {
        var fixture = CreateFixture(swaggerHasEndpoint: true, includeMock: true, catalogVersion: "2.0", baseUrls: new Dictionary<string, string> { ["dev"] = "http://example.test" });
        var pipeline = CreatePipeline();

        var result = await pipeline.RunAsync(new InlineArguments(
            WorkflowPath: fixture.WorkflowPath,
            Environment: "dev",
            CatalogPath: null), CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(result.Messages, message => message.Text.Contains("Catalog version", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_EnvironmentValidationFails_ReturnsError()
    {
        var fixture = CreateFixture(swaggerHasEndpoint: true, includeMock: true, catalogVersion: "1.0", baseUrls: new Dictionary<string, string> { ["prod"] = "http://example.test" });
        var pipeline = CreatePipeline();

        var result = await pipeline.RunAsync(new InlineArguments(
            WorkflowPath: fixture.WorkflowPath,
            Environment: "dev",
            CatalogPath: null), CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(result.Messages, message => message.Text.Contains("Environment validation failed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_EndpointValidationFails_ReturnsError()
    {
        var fixture = CreateFixture(swaggerHasEndpoint: false, includeMock: true, catalogVersion: "1.0", baseUrls: new Dictionary<string, string> { ["dev"] = "http://example.test" });
        var pipeline = CreatePipeline();

        var result = await pipeline.RunAsync(new InlineArguments(
            WorkflowPath: fixture.WorkflowPath,
            Environment: "dev",
            CatalogPath: null,
            DryRun: true), CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(result.Messages, message => message.Text.Contains("Endpoint validation failed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_DryRun_WithMockSelfJump_ReturnsError()
    {
        var fixture = CreateFixture(
            swaggerHasEndpoint: true,
            includeMock: true,
            catalogVersion: "1.0",
            baseUrls: new Dictionary<string, string> { ["dev"] = "http://example.test" },
            includeSelfJumpOnMock: true);
        var pipeline = CreatePipeline();

        var result = await pipeline.RunAsync(new InlineArguments(
            WorkflowPath: fixture.WorkflowPath,
            Environment: "dev",
            CatalogPath: null,
            DryRun: true,
            Mocked: false), CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(result.Messages, message => message.Text.Contains("mock status", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_DryRun_WorkflowStageResilience_ReturnsError()
    {
        var fixture = CreateFixture(
            swaggerHasEndpoint: true,
            includeMock: true,
            catalogVersion: "1.0",
            baseUrls: new Dictionary<string, string> { ["dev"] = "http://example.test" },
            includeWorkflowStageResilience: true);
        var pipeline = CreatePipeline();

        var result = await pipeline.RunAsync(new InlineArguments(
            WorkflowPath: fixture.WorkflowPath,
            Environment: "dev",
            CatalogPath: null,
            DryRun: true), CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(result.Messages, message =>
            message.Text.Contains("retry is only supported for endpoint stages", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Messages, message =>
            message.Text.Contains("circuitBreaker is only supported for endpoint stages", StringComparison.OrdinalIgnoreCase));
    }

    private static CliPipeline CreatePipeline()
    {
        var output = new TestOutputProvider();
        var factory = new CliServiceFactory(output);
        return new CliPipeline(
            new CliPathResolver(),
            new CliPlanPrinter(),
            new CliWorkflowEnvironmentValidator(),
            factory,
            new WorkflowConfigLoader(),
            new OpenTelemetryBootstrapper());
    }

    private static Fixture CreateFixture(
        bool swaggerHasEndpoint,
        bool includeMock,
        string catalogVersion,
        Dictionary<string, string> baseUrls,
        bool includeSelfJumpOnMock = false,
        bool includeWorkflowStageResilience = false)
    {
        var root = Path.Combine(Path.GetTempPath(), $"aos-cli-{Guid.NewGuid():N}");
        var workflows = Path.Combine(root, "workflows");
        Directory.CreateDirectory(workflows);

        var workflowPath = Path.Combine(workflows, "main.workflow");
        var swaggerPath = Path.Combine(root, "swagger.json");
        var catalogPath = Path.Combine(root, "api-catalog.json");

        var swaggerJson = swaggerHasEndpoint
            ? "{\"paths\":{\"/api/accounts\":{\"get\":{\"parameters\":[]}}}}"
            : "{\"paths\":{\"/api/other\":{\"get\":{\"parameters\":[]}}}}";
        File.WriteAllText(swaggerPath, swaggerJson);

        var swaggerUrl = new Uri(swaggerPath).AbsoluteUri;
        var baseUrlEntries = string.Join(",", baseUrls.Select(pair => $"\"{pair.Key}\": \"{pair.Value}\""));
        var catalogJson = $$"""
        [
          {
            "version": "{{catalogVersion}}",
            "baseUrl": {
              {{baseUrlEntries}}
            },
            "definitions": [
              {
                "name": "accounts",
                "swaggerUrl": "{{swaggerUrl}}",
                "baseUrl": null,
                "basePath": null
              }
            ]
          }
        ]
        """;
        File.WriteAllText(catalogPath, catalogJson);

        var workflowLines = new List<string>
        {
            "version: \"1.0\"",
            "id: \"wf-1\"",
            "name: \"Test\"",
            "references:",
        };

        if (includeWorkflowStageResilience)
        {
            workflowLines.Add("  workflows:");
            workflowLines.Add("    - name: \"child\"");
            workflowLines.Add("      path: \"./child.workflow\"");
        }

        workflowLines.Add("  apis:");
        workflowLines.Add("    - name: \"accounts\"");
        workflowLines.Add("      definition: \"accounts\"");

        if (includeWorkflowStageResilience)
        {
            workflowLines.Add("resilience:");
            workflowLines.Add("  retries:");
            workflowLines.Add("    standard:");
            workflowLines.Add("      maxRetries: 2");
            workflowLines.Add("      delayMs: 100");
            workflowLines.Add("  circuitBreakers:");
            workflowLines.Add("    standard:");
            workflowLines.Add("      failureThreshold: 2");
            workflowLines.Add("      breakMs: 1000");
        }

        workflowLines.Add("stages:");
        workflowLines.Add("  - name: \"list\"");
        workflowLines.Add("    kind: \"Endpoint\"");
        workflowLines.Add("    apiRef: \"accounts\"");
        workflowLines.Add("    endpoint: \"/api/accounts\"");
        workflowLines.Add("    httpVerb: \"GET\"");
        workflowLines.Add("    expectedStatus: 200");
        if (includeMock)
        {
            workflowLines.Add("    mock:");
            workflowLines.Add("      payload: |");
            workflowLines.Add("        { \"ok\": true }");
            if (includeSelfJumpOnMock)
            {
                workflowLines.Add("      status: 200");
            }
        }

        if (includeSelfJumpOnMock)
        {
            workflowLines.Add("    jumpOnStatus:");
            workflowLines.Add("      200: \"list\"");
        }

        if (includeWorkflowStageResilience)
        {
            workflowLines.Add("  - name: \"child\"");
            workflowLines.Add("    kind: \"Workflow\"");
            workflowLines.Add("    workflowRef: \"child\"");
            workflowLines.Add("    retry:");
            workflowLines.Add("      ref: \"standard\"");
            workflowLines.Add("      httpStatus: [500]");
            workflowLines.Add("    circuitBreaker:");
            workflowLines.Add("      ref: \"standard\"");
            workflowLines.Add("      httpStatus: [500]");
        }

        var workflowYaml = string.Join(Environment.NewLine, workflowLines);
        File.WriteAllText(workflowPath, workflowYaml);

        if (includeWorkflowStageResilience)
        {
            var childWorkflowPath = Path.Combine(workflows, "child.workflow");
            var childYaml = string.Join(Environment.NewLine, new[]
            {
                "version: \"1.0\"",
                "id: \"child-1\"",
                "name: \"Child\""
            });
            File.WriteAllText(childWorkflowPath, childYaml);
        }

        return new Fixture(workflowPath, catalogPath);
    }

    private sealed record Fixture(string WorkflowPath, string CatalogPath);

    private sealed class TestOutputProvider : ICliOutputProvider
    {
        public TextWriter Out { get; } = new StringWriter();
        public TextWriter Error { get; } = new StringWriter();
    }
}
