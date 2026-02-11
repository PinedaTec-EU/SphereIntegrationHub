using System.Text.Json;
using FluentAssertions;
using SphereIntegrationHub.MCP.Services.Integration;
using SphereIntegrationHub.MCP.Tests.TestHelpers;
using SphereIntegrationHub.MCP.Tools;
using Xunit;

namespace SphereIntegrationHub.MCP.Tests.Unit;

public class AnalysisToolsTests : IDisposable
{
    private readonly MockFileSystem _mockFs;
    private readonly SihServicesAdapter _adapter;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public AnalysisToolsTests()
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
    public async Task GetAvailableVariables_AtWorkflowStart_ReturnsInputsAndGlobals()
    {
        // Arrange
        var workflow = TestDataBuilder.CreateSampleWorkflow();
        _mockFs.AddWorkflow("analysis-test.workflow", workflow);

        var tool = new GetAvailableVariablesTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = "analysis-test.workflow"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("inputs", out var inputs).Should().BeTrue();
        json.TryGetProperty("globals", out var globals).Should().BeTrue();
        inputs.ValueKind.Should().NotBe(JsonValueKind.Null);
        globals.ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetAvailableVariables_AtSpecificStage_IncludesPreviousStageOutputs()
    {
        // Arrange
        var workflow = TestDataBuilder.CreateSampleWorkflow();
        _mockFs.AddWorkflow("stage-vars-test.workflow", workflow);

        var tool = new GetAvailableVariablesTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = "stage-vars-test.workflow",
            ["atStage"] = "get-accounts"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("context", out var context).Should().BeTrue();
        context.ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetAvailableVariables_WithEnvironmentVariables_IncludesEnvScope()
    {
        // Arrange
        var workflow = TestDataBuilder.CreateSampleWorkflow();
        _mockFs.AddWorkflow("env-test.workflow", workflow);

        var tool = new GetAvailableVariablesTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = "env-test.workflow"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("env", out var env).Should().BeTrue();
        env.ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetAvailableVariables_WithInvalidStage_ThrowsException()
    {
        // Arrange
        var workflow = TestDataBuilder.CreateSampleWorkflow();
        _mockFs.AddWorkflow("invalid-stage-test.workflow", workflow);

        var tool = new GetAvailableVariablesTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = "invalid-stage-test.workflow",
            ["atStage"] = "nonexistent-stage"
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => tool.ExecuteAsync(args));
    }

    [Fact]
    public async Task ResolveTemplateToken_WithInputToken_ReturnsInputSource()
    {
        // Arrange
        var workflow = TestDataBuilder.CreateSampleWorkflow();
        _mockFs.AddWorkflow("token-test.workflow", workflow);

        var tool = new ResolveTemplateTokenTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = "token-test.workflow",
            ["token"] = "input.userId"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("scope", out var scope).Should().BeTrue();
        json.TryGetProperty("variable", out var variable).Should().BeTrue();
        json.TryGetProperty("isAvailable", out var isAvailable).Should().BeTrue();
        scope.GetString().Should().Be("input");
        variable.GetString().Should().Be("userId");
        isAvailable.GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ResolveTemplateToken_WithBraces_ParsesCorrectly()
    {
        // Arrange
        var workflow = TestDataBuilder.CreateSampleWorkflow();
        _mockFs.AddWorkflow("braces-test.workflow", workflow);

        var tool = new ResolveTemplateTokenTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = "braces-test.workflow",
            ["token"] = "{{ input.userId }}"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("scope", out var scope).Should().BeTrue();
        json.TryGetProperty("variable", out var variable).Should().BeTrue();
        scope.GetString().Should().Be("input");
        variable.GetString().Should().Be("userId");
    }

    [Fact]
    public async Task ResolveTemplateToken_WithContextToken_ReturnsContextSource()
    {
        // Arrange
        var workflow = TestDataBuilder.CreateSampleWorkflow();
        _mockFs.AddWorkflow("context-test.workflow", workflow);

        var tool = new ResolveTemplateTokenTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = "context-test.workflow",
            ["token"] = "context.userData.id"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("scope", out var scope).Should().BeTrue();
        scope.GetString().Should().Be("context");
    }

    [Fact]
    public async Task ResolveTemplateToken_WithGlobalToken_ReturnsGlobalSource()
    {
        // Arrange
        var workflow = TestDataBuilder.CreateSampleWorkflow();
        _mockFs.AddWorkflow("global-test.workflow", workflow);

        var tool = new ResolveTemplateTokenTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = "global-test.workflow",
            ["token"] = "global.baseUrl"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("scope", out var scope).Should().BeTrue();
        json.TryGetProperty("variable", out var variable).Should().BeTrue();
        json.TryGetProperty("isAvailable", out var isAvailable).Should().BeTrue();
        scope.GetString().Should().Be("global");
        variable.GetString().Should().Be("baseUrl");
        isAvailable.GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ResolveTemplateToken_WithInvalidToken_ReturnsUnavailable()
    {
        // Arrange
        var workflow = TestDataBuilder.CreateSampleWorkflow();
        _mockFs.AddWorkflow("invalid-token-test.workflow", workflow);

        var tool = new ResolveTemplateTokenTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = "invalid-token-test.workflow",
            ["token"] = "nonexistent.field"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("isAvailable", out var isAvailable).Should().BeTrue();
        isAvailable.GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task AnalyzeContextFlow_WithMultipleStages_ShowsFlowBetweenStages()
    {
        // Arrange
        var workflow = TestDataBuilder.CreateSampleWorkflow();
        _mockFs.AddWorkflow("flow-test.workflow", workflow);

        var tool = new AnalyzeContextFlowTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = "flow-test.workflow"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("flow", out var flow).Should().BeTrue();
        flow.ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task AnalyzeContextFlow_IdentifiesStagesReadingContext()
    {
        // Arrange
        var workflow = TestDataBuilder.CreateSampleWorkflow();
        _mockFs.AddWorkflow("read-context-test.workflow", workflow);

        var tool = new AnalyzeContextFlowTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = "read-context-test.workflow"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("stagesReadingContext", out var stagesReadingContext).Should().BeTrue();
        stagesReadingContext.ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task AnalyzeContextFlow_IdentifiesStagesWritingContext()
    {
        // Arrange
        var workflow = TestDataBuilder.CreateSampleWorkflow();
        _mockFs.AddWorkflow("write-context-test.workflow", workflow);

        var tool = new AnalyzeContextFlowTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = "write-context-test.workflow"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("stagesWritingContext", out var stagesWritingContext).Should().BeTrue();
        stagesWritingContext.ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public void GetAvailableVariablesTool_HasCorrectMetadata()
    {
        // Arrange
        var tool = new GetAvailableVariablesTool(_adapter);

        // Assert
        tool.Name.Should().Be("get_available_variables");
        tool.Description.Should().NotBeNullOrEmpty();
        tool.InputSchema.Should().NotBeNull();
    }

    [Fact]
    public void ResolveTemplateTokenTool_HasCorrectMetadata()
    {
        // Arrange
        var tool = new ResolveTemplateTokenTool(_adapter);

        // Assert
        tool.Name.Should().Be("resolve_template_token");
        tool.Description.Should().NotBeNullOrEmpty();
        tool.InputSchema.Should().NotBeNull();
    }

    [Fact]
    public void AnalyzeContextFlowTool_HasCorrectMetadata()
    {
        // Arrange
        var tool = new AnalyzeContextFlowTool(_adapter);

        // Assert
        tool.Name.Should().Be("analyze_context_flow");
        tool.Description.Should().NotBeNullOrEmpty();
        tool.InputSchema.Should().NotBeNull();
    }

    [Fact]
    public async Task GetAvailableVariables_WithMissingFile_ThrowsException()
    {
        // Arrange
        var tool = new GetAvailableVariablesTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = "nonexistent.workflow"
        };

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => tool.ExecuteAsync(args));
    }

    public void Dispose()
    {
        _mockFs.Dispose();
    }
}
