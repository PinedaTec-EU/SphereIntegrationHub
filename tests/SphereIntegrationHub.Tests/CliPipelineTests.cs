using SphereIntegrationHub.cli;
using SphereIntegrationHub.Definitions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

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

    [Fact]
    public async Task RunAsync_MockedExecution_EmitsExecutionReportPaths()
    {
        var fixture = CreateFixture(swaggerHasEndpoint: true, includeMock: true, catalogVersion: "1.0", baseUrls: new Dictionary<string, string> { ["dev"] = "http://example.test" });
        var pipeline = CreatePipeline();

        var result = await pipeline.RunAsync(new InlineArguments(
            WorkflowPath: fixture.WorkflowPath,
            Environment: "dev",
            CatalogPath: null,
            Mocked: true,
            ReportFormat: "both",
            CaptureHttp: "bodies"), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.Messages, message => message.Text.Contains("JSON report:", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Messages, message => message.Text.Contains("HTML report:", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Messages, message => message.Text.Contains("Execution summary:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_DryRun_HealthCheckConfigured_ReportsHealthyEndpoint()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/health").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200));
        var fixture = CreateFixture(
            swaggerHasEndpoint: true,
            includeMock: true,
            catalogVersion: "1.0",
            baseUrls: new Dictionary<string, string> { ["dev"] = server.Url! },
            healthCheck: "/health",
            readiness: new ApiReadinessPolicyDefinition
            {
                MaxRetries = 2,
                DelayMs = 10,
                TimeoutMs = 2000,
                HttpStatus = [200]
            });
        var pipeline = CreatePipeline();

        var result = await pipeline.RunAsync(new InlineArguments(
            WorkflowPath: fixture.WorkflowPath,
            Environment: "dev",
            CatalogPath: null,
            DryRun: true), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.Messages, message => message.Text.Contains("Features:", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Messages, message => message.Text.Contains("Health check retry policy: enabled for 1/1 APIs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Messages, message => message.Text.Contains("Startup guard: enabled", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Messages, message => message.Text.Contains("API health checks:", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Messages, message => message.Text.Contains("Policy accounts: retries=2, delay=10ms, timeout=2000ms, healthyStatus=[200]", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Messages, message => message.Text.Contains($"OK accounts -> {server.Url}/health after 1 attempt(s)", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_DryRun_HealthCheckConfigured_ReportsFailingEndpointAndFails()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/health").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(503));
        var fixture = CreateFixture(
            swaggerHasEndpoint: true,
            includeMock: true,
            catalogVersion: "1.0",
            baseUrls: new Dictionary<string, string> { ["dev"] = server.Url! },
            healthCheck: "/health");
        var pipeline = CreatePipeline();

        var result = await pipeline.RunAsync(new InlineArguments(
            WorkflowPath: fixture.WorkflowPath,
            Environment: "dev",
            CatalogPath: null,
            DryRun: true), CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(result.Messages, message => message.Text.Contains($"Failed accounts -> {server.Url}/health after 1 attempt(s)", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Messages, message => message.Text.Contains("Dry-run completed successfully", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_DryRun_HealthCheckConfiguredWithReadinessRetry_RetriesUntilHealthy()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/health").UsingGet())
            .InScenario("health")
            .WillSetStateTo("ready")
            .RespondWith(Response.Create().WithStatusCode(503));
        server
            .Given(Request.Create().WithPath("/health").UsingGet())
            .InScenario("health")
            .WhenStateIs("ready")
            .RespondWith(Response.Create().WithStatusCode(204));
        var fixture = CreateFixture(
            swaggerHasEndpoint: true,
            includeMock: true,
            catalogVersion: "1.0",
            baseUrls: new Dictionary<string, string> { ["dev"] = server.Url! },
            healthCheck: "/health",
            readiness: new ApiReadinessPolicyDefinition
            {
                MaxRetries = 2,
                DelayMs = 1,
                HttpStatus = [204]
            });
        var pipeline = CreatePipeline();

        var result = await pipeline.RunAsync(new InlineArguments(
            WorkflowPath: fixture.WorkflowPath,
            Environment: "dev",
            CatalogPath: null,
            Verbose: true,
            DryRun: true), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.Messages, message => message.Text.Contains("attempt 1/3: HTTP 503", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Messages, message => message.Text.Contains("retrying in 1ms", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Messages, message => message.Text.Contains("attempt 2/3: HTTP 204", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Messages, message => message.Text.Contains($"OK accounts -> {server.Url}/health after 2 attempt(s)", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_DryRun_WithoutHealthCheck_DoesNotEmitHealthCheckMessages()
    {
        var fixture = CreateFixture(
            swaggerHasEndpoint: true,
            includeMock: true,
            catalogVersion: "1.0",
            baseUrls: new Dictionary<string, string> { ["dev"] = "http://example.test" });
        var pipeline = CreatePipeline();

        var result = await pipeline.RunAsync(new InlineArguments(
            WorkflowPath: fixture.WorkflowPath,
            Environment: "dev",
            CatalogPath: null,
            DryRun: true), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(result.Messages, message => message.Text.Contains("Features:", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Messages, message => message.Text.Contains("API health checks:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_DryRun_HealthCheckWithoutReadiness_ReportsReadinessFalse()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/health").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200));
        var fixture = CreateFixture(
            swaggerHasEndpoint: true,
            includeMock: true,
            catalogVersion: "1.0",
            baseUrls: new Dictionary<string, string> { ["dev"] = server.Url! },
            healthCheck: "/health");
        var pipeline = CreatePipeline();

        var result = await pipeline.RunAsync(new InlineArguments(
            WorkflowPath: fixture.WorkflowPath,
            Environment: "dev",
            CatalogPath: null,
            DryRun: true), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.Messages, message => message.Text.Contains("Features:", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Messages, message => message.Text.Contains("Health check retry policy: not configured for 1 APIs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Messages, message => message.Text.Contains("Startup guard: disabled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_DryRun_NestedWorkflowHealthCheckConfigured_ReportsEndpointFromChildWorkflow()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/health").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200));
        var fixture = CreateFixture(
            swaggerHasEndpoint: true,
            includeMock: true,
            catalogVersion: "1.0",
            baseUrls: new Dictionary<string, string> { ["dev"] = server.Url! },
            healthCheck: "/health",
            includeWorkflowStage: true,
            includeNestedApiOnly: true);
        var pipeline = CreatePipeline();

        var result = await pipeline.RunAsync(new InlineArguments(
            WorkflowPath: fixture.WorkflowPath,
            Environment: "dev",
            CatalogPath: null,
            DryRun: true), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.Messages, message => message.Text.Contains("API health checks:", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Messages, message => message.Text.Contains($"OK accounts -> {server.Url}/health", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_DryRun_CatalogHealthChecksIncludeDefinitionsNotReferencedByWorkflow()
    {
        using var accountsServer = WireMockServer.Start();
        accountsServer
            .Given(Request.Create().WithPath("/health").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200));
        using var ordersServer = WireMockServer.Start();
        ordersServer
            .Given(Request.Create().WithPath("/ready").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200));

        var fixture = CreateFixture(
            swaggerHasEndpoint: true,
            includeMock: true,
            catalogVersion: "1.0",
            baseUrls: new Dictionary<string, string> { ["dev"] = accountsServer.Url! },
            healthCheck: "/health",
            additionalDefinitions:
            [
                new CatalogDefinitionFixture(
                    "orders",
                    ordersServer.Url!,
                    "/ready",
                    "{\"paths\":{\"/api/orders\":{\"get\":{\"parameters\":[]}}}}")
            ]);
        var pipeline = CreatePipeline();

        var result = await pipeline.RunAsync(new InlineArguments(
            WorkflowPath: fixture.WorkflowPath,
            Environment: "dev",
            CatalogPath: null,
            DryRun: true), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.Messages, message => message.Text.Contains($"OK accounts -> {accountsServer.Url}/health", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Messages, message => message.Text.Contains($"OK orders -> {ordersServer.Url}/ready", StringComparison.OrdinalIgnoreCase));

        var cacheRoot = Path.Combine(Path.GetDirectoryName(fixture.CatalogPath)!, "cache", "1.0");
        Assert.True(File.Exists(Path.Combine(cacheRoot, "accounts.json")));
        Assert.True(File.Exists(Path.Combine(cacheRoot, "orders.json")));
    }

    [Fact]
    public async Task RunAsync_DryRun_ResolvesWorkflowReferencePathFromEnvironmentVariables()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aos-cli-dynamic-ref-{Guid.NewGuid():N}");
        var workflows = Path.Combine(root, "workflows");
        var tenantWorkflows = Path.Combine(workflows, "tenant-a");
        Directory.CreateDirectory(tenantWorkflows);

        var workflowPath = Path.Combine(workflows, "main.workflow");
        var childWorkflowPath = Path.Combine(tenantWorkflows, "child.workflow");
        var envFilePath = Path.Combine(workflows, ".env");
        var catalogPath = Path.Combine(root, "api-catalog.json");
        var swaggerPath = Path.Combine(root, "accounts.swagger.json");

        File.WriteAllText(swaggerPath, "{\"paths\":{\"/api/accounts\":{\"get\":{\"parameters\":[]}}}}");
        File.WriteAllText(catalogPath, $$"""
        [
          {
            "version": "1.0",
            "definitions": [
              {
                "name": "accounts",
                "swaggerUrl": "{{new Uri(swaggerPath).AbsoluteUri}}",
                "healthCheck": null,
                "readiness": null,
                "baseUrl": {
                  "dev": "http://example.test"
                },
                "basePath": null
              }
            ]
          }
        ]
        """);

        File.WriteAllText(envFilePath, "CHILD_TENANT=tenant-a");
        File.WriteAllText(childWorkflowPath, """
version: "1.0"
id: "child-1"
name: "Child"
references:
  apis:
    - name: "accounts"
      definition: "accounts"
stages:
  - name: "list"
    kind: "Endpoint"
    apiRef: "accounts"
    endpoint: "/api/accounts"
    httpVerb: "GET"
    expectedStatus: 200
    mock:
      payload: |
        { "ok": true }
""");

        File.WriteAllText(workflowPath, """
version: "1.0"
id: "wf-1"
name: "Test"
references:
  environmentFile: "./.env"
  workflows:
    - name: "child"
      path: "./{{env:CHILD_TENANT}}/child.workflow"
stages:
  - name: "child"
    kind: "Workflow"
    workflowRef: "child"
""");

        var pipeline = CreatePipeline();
        var result = await pipeline.RunAsync(new InlineArguments(
            WorkflowPath: workflowPath,
            Environment: "dev",
            CatalogPath: null,
            DryRun: true,
            Mocked: true), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.Messages, message => message.Text.Contains("Dry-run completed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_DryRun_ResolvesWorkflowReferencePathFromDerivedEnvironmentVariables()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aos-cli-derived-ref-{Guid.NewGuid():N}");
        var workflows = Path.Combine(root, "workflows");
        var tenantWorkflows = Path.Combine(workflows, "tenant-a");
        Directory.CreateDirectory(tenantWorkflows);

        var workflowPath = Path.Combine(workflows, "main.workflow");
        var childWorkflowPath = Path.Combine(tenantWorkflows, "child.workflow");
        var envFilePath = Path.Combine(workflows, ".env");
        var catalogPath = Path.Combine(root, "api-catalog.json");

        File.WriteAllText(catalogPath, """
        [
          {
            "version": "1.0",
            "definitions": []
          }
        ]
        """);

        File.WriteAllText(envFilePath, """
        BASE_WORKFLOWS=./tenant-a
        CHILD_WORKFLOW_DIR={{env:BASE_WORKFLOWS}}
        """);
        File.WriteAllText(childWorkflowPath, """
version: "1.0"
id: "child-1"
name: "Child"
stages: []
""");

        File.WriteAllText(workflowPath, """
version: "1.0"
id: "wf-1"
name: "Test"
references:
  environmentFile: "./.env"
  workflows:
    - name: "child"
      path: "{{env:CHILD_WORKFLOW_DIR}}/child.workflow"
stages:
  - name: "child"
    kind: "Workflow"
    workflowRef: "child"
""");

        var pipeline = CreatePipeline();
        var result = await pipeline.RunAsync(new InlineArguments(
            WorkflowPath: workflowPath,
            Environment: "dev",
            CatalogPath: catalogPath,
            DryRun: true,
            Verbose: true), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.Messages, message => message.Text.Contains("CHILD_WORKFLOW_DIR: ./tenant-a", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Messages, message => message.Text.Contains(childWorkflowPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_DryRun_WarnsWhenWorkflowPathDependsOnDeferredBusinessVariable()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aos-cli-dynamic-warning-{Guid.NewGuid():N}");
        var workflows = Path.Combine(root, "workflows");
        Directory.CreateDirectory(workflows);

        var workflowPath = Path.Combine(workflows, "main.workflow");
        var catalogPath = Path.Combine(root, "api-catalog.json");
        File.WriteAllText(catalogPath, """
        [
          {
            "version": "1.0",
            "definitions": []
          }
        ]
        """);

        File.WriteAllText(workflowPath, """
version: "1.0"
id: "wf-1"
name: "Test"
references:
  workflows:
    - name: "child"
      path: "./{{input.tenant}}/child.workflow"
stages:
  - name: "child"
    kind: "Workflow"
    workflowRef: "child"
""");

        var pipeline = CreatePipeline();
        var result = await pipeline.RunAsync(new InlineArguments(
            WorkflowPath: workflowPath,
            Environment: "dev",
            CatalogPath: catalogPath,
            DryRun: true), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.Messages, message => message.Text.Contains("Warning:", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Messages, message => message.Text.Contains("will be resolved at runtime", StringComparison.OrdinalIgnoreCase));
    }

    private static CliPipeline CreatePipeline()
    {
        var output = new TestOutputProvider();
        var factory = new CliServiceFactory(output);
        return new CliPipeline(
            new CliPathResolver(),
            new CliPlanPrinter(),
            new CliWorkflowEnvironmentValidator(),
            output,
            factory,
            new WorkflowConfigLoader(),
            new OpenTelemetryBootstrapper());
    }

    private static Fixture CreateFixture(
        bool swaggerHasEndpoint,
        bool includeMock,
        string catalogVersion,
        Dictionary<string, string> baseUrls,
        string? healthCheck = null,
        ApiReadinessPolicyDefinition? readiness = null,
        bool includeSelfJumpOnMock = false,
        bool includeWorkflowStageResilience = false,
        bool includeWorkflowStage = false,
        bool includeNestedApiOnly = false,
        IReadOnlyList<CatalogDefinitionFixture>? additionalDefinitions = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"aos-cli-{Guid.NewGuid():N}");
        var workflows = Path.Combine(root, "workflows");
        Directory.CreateDirectory(workflows);

        var workflowPath = Path.Combine(workflows, "main.workflow");
        var catalogPath = Path.Combine(root, "api-catalog.json");
        var baseUrlEntries = string.Join(",", baseUrls.Select(pair => $"\"{pair.Key}\": \"{pair.Value}\""));

        var definitionJsonEntries = new List<string>();
        var accountSwaggerPath = Path.Combine(root, "accounts.swagger.json");
        var accountSwaggerJson = swaggerHasEndpoint
            ? "{\"paths\":{\"/api/accounts\":{\"get\":{\"parameters\":[]}}}}"
            : "{\"paths\":{\"/api/other\":{\"get\":{\"parameters\":[]}}}}";
        File.WriteAllText(accountSwaggerPath, accountSwaggerJson);
        definitionJsonEntries.Add($$"""
              {
                "name": "accounts",
                "swaggerUrl": "{{new Uri(accountSwaggerPath).AbsoluteUri}}",
                "healthCheck": {{(healthCheck is null ? "null" : $"\"{healthCheck}\"")}},
                "readiness": {{FormatReadinessJson(readiness)}},
                "baseUrl": {
                  {{baseUrlEntries}}
                },
                "basePath": null
              }
        """);

        if (additionalDefinitions is not null)
        {
            foreach (var definition in additionalDefinitions)
            {
                var swaggerPath = Path.Combine(root, $"{definition.Name}.swagger.json");
                File.WriteAllText(swaggerPath, definition.SwaggerJson);
                definitionJsonEntries.Add($$"""
              {
                "name": "{{definition.Name}}",
                "swaggerUrl": "{{new Uri(swaggerPath).AbsoluteUri}}",
                "healthCheck": {{(definition.HealthCheck is null ? "null" : $"\"{definition.HealthCheck}\"")}},
                "readiness": {{FormatReadinessJson(definition.Readiness)}},
                "baseUrl": {
                  "dev": "{{definition.BaseUrl}}"
                },
                "basePath": null
              }
        """);
            }
        }

        var catalogJson = $$"""
        [
          {
            "version": "{{catalogVersion}}",
            "definitions": [
        {{string.Join("," + Environment.NewLine, definitionJsonEntries)}}
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

        if (includeWorkflowStageResilience || includeWorkflowStage || includeNestedApiOnly)
        {
            workflowLines.Add("  workflows:");
            workflowLines.Add("    - name: \"child\"");
            workflowLines.Add("      path: \"./child.workflow\"");
        }

        if (!includeNestedApiOnly)
        {
            workflowLines.Add("  apis:");
            workflowLines.Add("    - name: \"accounts\"");
            workflowLines.Add("      definition: \"accounts\"");
        }

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
        if (!includeNestedApiOnly)
        {
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
        }

        if (includeWorkflowStageResilience || includeWorkflowStage || includeNestedApiOnly)
        {
            workflowLines.Add("  - name: \"child\"");
            workflowLines.Add("    kind: \"Workflow\"");
            workflowLines.Add("    workflowRef: \"child\"");
            if (includeWorkflowStageResilience)
            {
                workflowLines.Add("    retry:");
                workflowLines.Add("      ref: \"standard\"");
                workflowLines.Add("      httpStatus: [500]");
                workflowLines.Add("    circuitBreaker:");
                workflowLines.Add("      ref: \"standard\"");
                workflowLines.Add("      httpStatus: [500]");
            }
        }

        var workflowYaml = string.Join(Environment.NewLine, workflowLines);
        File.WriteAllText(workflowPath, workflowYaml);

        if (includeWorkflowStageResilience || includeWorkflowStage || includeNestedApiOnly)
        {
            var childWorkflowPath = Path.Combine(workflows, "child.workflow");
            var childLines = new List<string>
            {
                "version: \"1.0\"",
                "id: \"child-1\"",
                "name: \"Child\""
            };

            if (includeNestedApiOnly)
            {
                childLines.Add("references:");
                childLines.Add("  apis:");
                childLines.Add("    - name: \"accounts\"");
                childLines.Add("      definition: \"accounts\"");
                childLines.Add("stages:");
                childLines.Add("  - name: \"list\"");
                childLines.Add("    kind: \"Endpoint\"");
                childLines.Add("    apiRef: \"accounts\"");
                childLines.Add("    endpoint: \"/api/accounts\"");
                childLines.Add("    httpVerb: \"GET\"");
                childLines.Add("    expectedStatus: 200");
                if (includeMock)
                {
                    childLines.Add("    mock:");
                    childLines.Add("      payload: |");
                    childLines.Add("        { \"ok\": true }");
                }
            }

            var childYaml = string.Join(Environment.NewLine, childLines);
            File.WriteAllText(childWorkflowPath, childYaml);
        }

        return new Fixture(workflowPath, catalogPath);
    }

    private sealed record Fixture(string WorkflowPath, string CatalogPath);

    private static string FormatReadinessJson(ApiReadinessPolicyDefinition? readiness)
        => readiness is null
            ? "null"
            : $$"""
            {
              "maxRetries": {{(readiness.MaxRetries?.ToString() ?? "null")}},
              "delayMs": {{(readiness.DelayMs?.ToString() ?? "null")}},
              "timeoutMs": {{(readiness.TimeoutMs?.ToString() ?? "null")}},
              "httpStatus": {{(readiness.HttpStatus is { Length: > 0 } ? $"[{string.Join(", ", readiness.HttpStatus)}]" : "null")}}
            }
            """;

    private sealed record CatalogDefinitionFixture(string Name, string BaseUrl, string? HealthCheck, string SwaggerJson, ApiReadinessPolicyDefinition? Readiness = null);

    private sealed class TestOutputProvider : ICliOutputProvider
    {
        public TextWriter Out { get; } = new StringWriter();
        public TextWriter Error { get; } = new StringWriter();
        public bool UseColors => false;
    }
}
