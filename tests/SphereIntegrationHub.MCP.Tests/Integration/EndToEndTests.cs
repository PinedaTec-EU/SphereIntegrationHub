using System.Text.Json;
using FluentAssertions;
using SphereIntegrationHub.MCP.Services.Integration;
using SphereIntegrationHub.MCP.Tests.TestHelpers;
using SphereIntegrationHub.MCP.Tools;
using Xunit;

namespace SphereIntegrationHub.MCP.Tests.Integration;

/// <summary>
/// End-to-end integration tests for complete workflows
/// </summary>
public class EndToEndTests : IDisposable
{
    private readonly MockFileSystem _mockFs;
    private readonly SihServicesAdapter _adapter;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public EndToEndTests()
    {
        _mockFs = new MockFileSystem();
        _mockFs.AddApiCatalog(TestDataBuilder.CreateSampleApiCatalog());
        _mockFs.SetupCachedVersions("3.10", "3.11");
        _adapter = new SihServicesAdapter(_mockFs.RootPath);
    }

    private static JsonElement ToJson(object result)
    {
        var json = JsonSerializer.Serialize(result, JsonOpts);
        return JsonDocument.Parse(json).RootElement;
    }

    [Fact]
    public async Task CompleteWorkflow_ListApis_AnalyzeEndpoints_GenerateWorkflow_Validate()
    {
        // Arrange - Create tools
        var listApisTool = new ListApiCatalogVersionsTool(_adapter);
        var getApisTool = new GetApiDefinitionsTool(_adapter);
        var getEndpointsTool = new GetApiEndpointsTool(_adapter);
        var generateTool = new GenerateEndpointStageTool(_adapter);
        var validateTool = new ValidateWorkflowTool(_adapter);

        // Act - Step 1: List available APIs
        var versionsResult = await listApisTool.ExecuteAsync(new Dictionary<string, object>());
        versionsResult.Should().NotBeNull();
        var versionsJson = ToJson(versionsResult);
        versionsJson.TryGetProperty("versions", out var versionsEl).Should().BeTrue();
        versionsEl.ValueKind.Should().Be(JsonValueKind.Array);
        versionsEl.GetArrayLength().Should().BeGreaterThan(0);
        var version = versionsEl[0].GetString();

        // Act - Step 2: Get API definitions
        var apisResult = await getApisTool.ExecuteAsync(new Dictionary<string, object>
        {
            ["version"] = version!
        });
        apisResult.Should().NotBeNull();

        // Act - Step 3: Get endpoints
        var endpointsResult = await getEndpointsTool.ExecuteAsync(new Dictionary<string, object>
        {
            ["version"] = version!,
            ["apiName"] = "AccountsAPI"
        });
        endpointsResult.Should().NotBeNull();

        // Act - Step 4: Generate stage
        var stageResult = await generateTool.ExecuteAsync(new Dictionary<string, object>
        {
            ["version"] = version!,
            ["apiName"] = "AccountsAPI",
            ["endpoint"] = "/api/accounts",
            ["httpVerb"] = "GET"
        });
        stageResult.Should().NotBeNull();

        // Act - Step 5: Create and validate workflow
        var workflow = TestDataBuilder.CreateSampleWorkflow("e2e-test");
        _mockFs.AddWorkflow("e2e-test.workflow", workflow);

        var validationResult = await validateTool.ExecuteAsync(new Dictionary<string, object>
        {
            ["workflowPath"] = "e2e-test.workflow"
        });

        // Assert
        validationResult.Should().NotBeNull();
        var validationJson = ToJson(validationResult);
        validationJson.TryGetProperty("isValid", out var isValidEl).Should().BeTrue();
        isValidEl.GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task SemanticAnalysisPipeline_AnalyzeDependencies_InferDataFlow_SuggestWorkflow()
    {
        // Arrange
        var analyzeDepsTool = new AnalyzeEndpointDependenciesTool(_adapter);
        var inferDataFlowTool = new InferDataFlowTool(_adapter);
        var suggestWorkflowTool = new SuggestWorkflowFromGoalTool(_adapter);

        // Act - Step 1: Analyze endpoint dependencies
        var depsResult = await analyzeDepsTool.ExecuteAsync(new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["apiName"] = "AccountsAPI",
            ["endpoint"] = "/api/accounts/{id}",
            ["httpVerb"] = "GET"
        });
        depsResult.Should().NotBeNull();

        // Act - Step 2: Infer data flow between endpoints
        var endpoints = new[]
        {
            new { apiName = "AccountsAPI", endpoint = "/api/accounts", httpVerb = "GET" },
            new { apiName = "AccountsAPI", endpoint = "/api/accounts/{id}", httpVerb = "GET" }
        };

        var dataFlowResult = await inferDataFlowTool.ExecuteAsync(new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["endpoints"] = JsonSerializer.SerializeToElement(endpoints)
        });
        dataFlowResult.Should().NotBeNull();

        // Act - Step 3: Suggest workflow from goal
        var workflowResult = await suggestWorkflowTool.ExecuteAsync(new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["goal"] = "Get all accounts and retrieve details for the first one"
        });

        // Assert
        workflowResult.Should().NotBeNull();
        var workflowJson = ToJson(workflowResult);
        workflowJson.TryGetProperty("workflowYaml", out var workflowYamlEl).Should().BeTrue();
        workflowYamlEl.ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task AutonomousSystemGeneration_PatternDetection_Synthesis_Optimization()
    {
        // Arrange
        var detectPatternsTool = new DetectApiPatternsTool(_adapter);
        var synthesizeTool = new SynthesizeSystemFromDescriptionTool(_adapter);
        var optimizeTool = new SuggestOptimizationsTool(_adapter);

        // Act - Step 1: Detect API patterns
        var patternsResult = await detectPatternsTool.ExecuteAsync(new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["apiName"] = "AccountsAPI"
        });
        patternsResult.Should().NotBeNull();

        // Act - Step 2: Synthesize complete system
        var systemResult = await synthesizeTool.ExecuteAsync(new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["description"] = "Build a complete account management system with CRUD operations and data validation"
        });
        systemResult.Should().NotBeNull();
        var systemJson = ToJson(systemResult);
        systemJson.TryGetProperty("workflows", out var workflowsEl).Should().BeTrue();
        workflowsEl.ValueKind.Should().Be(JsonValueKind.Array);

        // Act - Step 3: Optimize generated workflows
        if (workflowsEl.GetArrayLength() > 0)
        {
            var firstWorkflow = workflowsEl[0];
            if (firstWorkflow.TryGetProperty("fullYaml", out var fullYamlEl) &&
                fullYamlEl.ValueKind == JsonValueKind.String)
            {
                var workflowYaml = fullYamlEl.GetString();

                if (!string.IsNullOrEmpty(workflowYaml))
                {
                    var optimizationResult = await optimizeTool.ExecuteAsync(new Dictionary<string, object>
                    {
                        ["workflowYaml"] = workflowYaml
                    });

                    // Assert
                    optimizationResult.Should().NotBeNull();
                    var optimizationJson = ToJson(optimizationResult);
                    optimizationJson.TryGetProperty("optimizations", out var optimizationsEl).Should().BeTrue();
                    optimizationsEl.ValueKind.Should().NotBe(JsonValueKind.Null);
                }
            }
        }
    }

    [Fact]
    public async Task OptimizationSuggestionsPipeline_AnalyzeWorkflow_ApplySuggestions()
    {
        // Arrange
        var workflow = TestDataBuilder.CreateSampleWorkflow();
        _mockFs.AddWorkflow("optimize-test.workflow", workflow);

        var validateTool = new ValidateWorkflowTool(_adapter);
        var optimizeTool = new SuggestOptimizationsTool(_adapter);
        var analyzeVarsTool = new GetAvailableVariablesTool(_adapter);

        // Act - Step 1: Validate workflow
        var validationResult = await validateTool.ExecuteAsync(new Dictionary<string, object>
        {
            ["workflowPath"] = "optimize-test.workflow"
        });
        validationResult.Should().NotBeNull();

        // Act - Step 2: Analyze available variables
        var varsResult = await analyzeVarsTool.ExecuteAsync(new Dictionary<string, object>
        {
            ["workflowPath"] = "optimize-test.workflow"
        });
        varsResult.Should().NotBeNull();

        // Act - Step 3: Get optimization suggestions
        var optimizationResult = await optimizeTool.ExecuteAsync(new Dictionary<string, object>
        {
            ["workflowYaml"] = workflow
        });

        // Assert
        optimizationResult.Should().NotBeNull();
        var optimizationJson = ToJson(optimizationResult);
        optimizationJson.TryGetProperty("currentMetrics", out var currentMetricsEl).Should().BeTrue();
        optimizationJson.TryGetProperty("projectedMetrics", out var projectedMetricsEl).Should().BeTrue();
        currentMetricsEl.ValueKind.Should().NotBe(JsonValueKind.Null);
        projectedMetricsEl.ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task CrudWorkflowGeneration_DetectPattern_GenerateCrud_Validate()
    {
        // Arrange
        var detectTool = new DetectApiPatternsTool(_adapter);
        var generateCrudTool = new GenerateCrudWorkflowTool(_adapter);

        // Act - Step 1: Detect CRUD patterns
        var patternsResult = await detectTool.ExecuteAsync(new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["apiName"] = "AccountsAPI"
        });
        patternsResult.Should().NotBeNull();

        // Act - Step 2: Generate CRUD workflow
        var operations = new[] { "create", "read", "update", "delete", "list" };
        var crudResult = await generateCrudTool.ExecuteAsync(new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["apiName"] = "AccountsAPI",
            ["resource"] = "accounts",
            ["operations"] = JsonSerializer.SerializeToElement(operations)
        });

        // Assert
        crudResult.Should().NotBeNull();
        var crudJson = ToJson(crudResult);
        crudJson.TryGetProperty("yaml", out var yamlEl).Should().BeTrue();
        var workflowYaml = yamlEl.GetString();
        workflowYaml.Should().NotBeNullOrEmpty();
        workflowYaml.Should().Contain("stages:");
    }

    [Fact]
    public async Task ErrorHandling_InvalidWorkflow_ExplainError_ProvideSuggestions()
    {
        // Arrange
        var invalidWorkflow = TestDataBuilder.CreateSampleWorkflow(includeErrors: true);
        _mockFs.AddWorkflow("invalid.workflow", invalidWorkflow);

        var validateTool = new ValidateWorkflowTool(_adapter);
        var explainTool = new ExplainValidationErrorTool(_adapter);

        // Act - Step 1: Validate workflow (should fail)
        var validationResult = await validateTool.ExecuteAsync(new Dictionary<string, object>
        {
            ["workflowPath"] = "invalid.workflow"
        });

        var validationJson = ToJson(validationResult);
        validationJson.TryGetProperty("isValid", out var isValidEl).Should().BeTrue();
        isValidEl.GetBoolean().Should().BeFalse();

        validationJson.TryGetProperty("errors", out var errorsEl).Should().BeTrue();
        errorsEl.ValueKind.Should().Be(JsonValueKind.Array);
        errorsEl.GetArrayLength().Should().BeGreaterThan(0);

        // Act - Step 2: Explain first error
        if (errorsEl.GetArrayLength() > 0)
        {
            var firstError = errorsEl[0];
            var errorMessage = firstError.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String
                ? msgEl.GetString() ?? "Unknown error"
                : "Unknown error";

            var explanationResult = await explainTool.ExecuteAsync(new Dictionary<string, object>
            {
                ["errorCategory"] = "Stage",
                ["errorMessage"] = errorMessage
            });

            // Assert
            explanationResult.Should().NotBeNull();
            var explanationJson = ToJson(explanationResult);
            explanationJson.TryGetProperty("explanation", out var explanationEl).Should().BeTrue();
            explanationJson.TryGetProperty("suggestions", out var suggestionsEl).Should().BeTrue();
            explanationEl.ValueKind.Should().NotBe(JsonValueKind.Null);
            suggestionsEl.ValueKind.Should().NotBe(JsonValueKind.Null);
        }
    }

    [Fact]
    public async Task ResilienceConfiguration_GetPluginCapabilities_SuggestResilience()
    {
        // Arrange
        var capabilitiesTool = new GetPluginCapabilitiesTool(_adapter);
        var resilienceTool = new SuggestResilienceConfigTool(_adapter);

        // Act - Step 1: Get plugin capabilities
        var capabilitiesResult = await capabilitiesTool.ExecuteAsync(new Dictionary<string, object>
        {
            ["pluginType"] = "api"
        });
        capabilitiesResult.Should().NotBeNull();

        // Act - Step 2: Suggest resilience configuration
        var resilienceResult = await resilienceTool.ExecuteAsync(new Dictionary<string, object>
        {
            ["stageType"] = "api",
            ["operation"] = "GET /api/accounts",
            ["critical"] = true
        });

        // Assert
        resilienceResult.Should().NotBeNull();
        var resilienceJson = ToJson(resilienceResult);
        resilienceJson.TryGetProperty("recommendations", out var recommendationsEl).Should().BeTrue();
        recommendationsEl.ValueKind.Should().NotBe(JsonValueKind.Null);

        recommendationsEl.TryGetProperty("retry", out var retryEl).Should().BeTrue();
        recommendationsEl.TryGetProperty("timeout", out var timeoutEl).Should().BeTrue();
        recommendationsEl.TryGetProperty("circuitBreaker", out var circuitBreakerEl).Should().BeTrue();

        retryEl.ValueKind.Should().NotBe(JsonValueKind.Null);
        timeoutEl.ValueKind.Should().NotBe(JsonValueKind.Null);
        circuitBreakerEl.ValueKind.Should().NotBe(JsonValueKind.Null);

        // Circuit breaker should be enabled for critical operations
        circuitBreakerEl.TryGetProperty("enabled", out var enabledEl).Should().BeTrue();
        enabledEl.GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task WorkflowReference_ListWorkflows_GetInputsOutputs_AnalyzeContextFlow()
    {
        // Arrange
        var workflow = TestDataBuilder.CreateSampleWorkflow();
        _mockFs.AddWorkflow("reference-test.workflow", workflow);

        var listWorkflowsTool = new ListAvailableWorkflowsTool(_adapter);
        var getIoTool = new GetWorkflowInputsOutputsTool(_adapter);
        var analyzeFlowTool = new AnalyzeContextFlowTool(_adapter);

        // Act - Step 1: List available workflows
        var workflowsResult = await listWorkflowsTool.ExecuteAsync(new Dictionary<string, object>());
        workflowsResult.Should().NotBeNull();

        // Act - Step 2: Get inputs/outputs
        var ioResult = await getIoTool.ExecuteAsync(new Dictionary<string, object>
        {
            ["workflowPath"] = "reference-test.workflow"
        });
        ioResult.Should().NotBeNull();

        // Act - Step 3: Analyze context flow
        var flowResult = await analyzeFlowTool.ExecuteAsync(new Dictionary<string, object>
        {
            ["workflowPath"] = "reference-test.workflow"
        });

        // Assert
        flowResult.Should().NotBeNull();
        var flowJson = ToJson(flowResult);
        flowJson.TryGetProperty("flow", out var flowEl).Should().BeTrue();
        flowEl.ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    public void Dispose()
    {
        _mockFs.Dispose();
    }
}
