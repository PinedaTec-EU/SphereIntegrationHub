using System.Text.Json;
using FluentAssertions;
using SphereIntegrationHub.MCP.Services.Integration;
using SphereIntegrationHub.MCP.Tests.TestHelpers;
using SphereIntegrationHub.MCP.Tools;
using Xunit;

namespace SphereIntegrationHub.MCP.Tests.Unit;

public class ValidationToolsTests : IDisposable
{
    private readonly MockFileSystem _mockFs;
    private readonly SihServicesAdapter _adapter;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public ValidationToolsTests()
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
    public async Task ValidateWorkflow_WithValidWorkflow_ReturnsSuccess()
    {
        // Arrange
        var validWorkflow = TestDataBuilder.CreateSampleWorkflow();
        _mockFs.AddWorkflow("valid-workflow.workflow", validWorkflow);

        var tool = new ValidateWorkflowTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = "valid-workflow.workflow"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("isValid", out var isValidEl).Should().BeTrue();
        json.TryGetProperty("errors", out var errorsEl).Should().BeTrue();

        isValidEl.GetBoolean().Should().BeTrue();
        errorsEl.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ValidateWorkflow_WithInvalidWorkflow_ReturnsErrors()
    {
        // Arrange
        var invalidWorkflow = TestDataBuilder.CreateSampleWorkflow(includeErrors: true);
        _mockFs.AddWorkflow("invalid-workflow.workflow", invalidWorkflow);

        var tool = new ValidateWorkflowTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = "invalid-workflow.workflow"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("isValid", out var isValidEl).Should().BeTrue();
        json.TryGetProperty("errors", out var errorsEl).Should().BeTrue();

        isValidEl.GetBoolean().Should().BeFalse();
        errorsEl.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ValidateWorkflow_WithAbsolutePath_WorksCorrectly()
    {
        // Arrange
        var validWorkflow = TestDataBuilder.CreateSampleWorkflow();
        var absolutePath = Path.Combine(_mockFs.WorkflowsPath, "absolute-test.workflow");
        File.WriteAllText(absolutePath, validWorkflow);

        var tool = new ValidateWorkflowTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = absolutePath
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("isValid", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ValidateWorkflow_WithMissingFile_ThrowsException()
    {
        // Arrange
        var tool = new ValidateWorkflowTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = "nonexistent.workflow"
        };

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => tool.ExecuteAsync(args));
    }

    [Fact]
    public async Task ValidateWorkflow_WithMalformedYaml_ReturnsError()
    {
        // Arrange
        var malformedYaml = @"
name: test
description: [this is malformed
  - invalid yaml structure
";
        _mockFs.AddWorkflow("malformed.workflow", malformedYaml);

        var tool = new ValidateWorkflowTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = "malformed.workflow"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("isValid", out var isValidEl).Should().BeTrue();
        isValidEl.GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void ValidateStage_WithValidStage_ReturnsSuccess()
    {
        // Arrange
        var validStage = TestDataBuilder.CreateSampleStage();
        var tool = new ValidateStageTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["stageDefinition"] = validStage
        };

        // Act
        var result = tool.ExecuteAsync(args).Result;
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("isValid", out var isValidEl).Should().BeTrue();
        isValidEl.GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void ValidateStage_WithInvalidStage_ReturnsErrors()
    {
        // Arrange
        var invalidStage = TestDataBuilder.CreateInvalidStage();
        var tool = new ValidateStageTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["stageDefinition"] = invalidStage
        };

        // Act
        var result = tool.ExecuteAsync(args).Result;
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("isValid", out var isValidEl).Should().BeTrue();
        json.TryGetProperty("errors", out var errorsEl).Should().BeTrue();

        isValidEl.GetBoolean().Should().BeFalse();
        errorsEl.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public void ValidateStage_WithMissingName_ReturnsError()
    {
        // Arrange
        var stageWithoutName = new Dictionary<string, object>
        {
            ["plugin"] = "HttpClientPlugin",
            ["config"] = new Dictionary<string, object>
            {
                ["url"] = "https://api.example.com",
                ["method"] = "GET"
            }
        };

        var tool = new ValidateStageTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["stageDefinition"] = stageWithoutName
        };

        // Act
        var result = tool.ExecuteAsync(args).Result;
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("isValid", out var isValidEl).Should().BeTrue();
        isValidEl.GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task PlanWorkflowExecution_WithValidWorkflow_ReturnsExecutionPlan()
    {
        // Arrange
        var validWorkflow = TestDataBuilder.CreateSampleWorkflow();
        _mockFs.AddWorkflow("plan-test.workflow", validWorkflow);

        var tool = new PlanWorkflowExecutionTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = "plan-test.workflow"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("executionOrder", out var execOrderEl).Should().BeTrue();
        json.TryGetProperty("stages", out var stagesEl).Should().BeTrue();

        execOrderEl.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task PlanWorkflowExecution_WithDependencies_ReturnsCorrectOrder()
    {
        // Arrange
        var workflowWithDeps = TestDataBuilder.CreateSampleWorkflow();
        _mockFs.AddWorkflow("deps-test.workflow", workflowWithDeps);

        var tool = new PlanWorkflowExecutionTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = "deps-test.workflow"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("executionOrder", out var execOrderEl).Should().BeTrue();
        execOrderEl.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task PlanWorkflowExecution_WithMissingFile_ThrowsException()
    {
        // Arrange
        var tool = new PlanWorkflowExecutionTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = "missing-plan.workflow"
        };

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => tool.ExecuteAsync(args));
    }

    [Fact]
    public void ValidateWorkflowTool_HasCorrectMetadata()
    {
        // Arrange
        var tool = new ValidateWorkflowTool(_adapter);

        // Assert
        tool.Name.Should().Be("validate_workflow");
        tool.Description.Should().NotBeNullOrEmpty();
        tool.InputSchema.Should().NotBeNull();
    }

    [Fact]
    public void ValidateStageTool_HasCorrectMetadata()
    {
        // Arrange
        var tool = new ValidateStageTool(_adapter);

        // Assert
        tool.Name.Should().Be("validate_stage");
        tool.Description.Should().NotBeNullOrEmpty();
        tool.InputSchema.Should().NotBeNull();
    }

    [Fact]
    public void PlanWorkflowExecutionTool_HasCorrectMetadata()
    {
        // Arrange
        var tool = new PlanWorkflowExecutionTool(_adapter);

        // Assert
        tool.Name.Should().Be("plan_workflow_execution");
        tool.Description.Should().NotBeNullOrEmpty();
        tool.InputSchema.Should().NotBeNull();
    }

    [Fact]
    public async Task ValidateWorkflow_WithMissingRequiredFields_ReturnsSpecificErrors()
    {
        // Arrange
        var workflowMissingName = @"
description: Missing name field
inputs:
  - test

stages:
  - name: stage1
    plugin: HttpClientPlugin
    config:
      url: https://test.com
      method: GET
";
        _mockFs.AddWorkflow("missing-fields.workflow", workflowMissingName);

        var tool = new ValidateWorkflowTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = "missing-fields.workflow"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("isValid", out var isValidEl).Should().BeTrue();
        json.TryGetProperty("errors", out var errorsEl).Should().BeTrue();

        isValidEl.GetBoolean().Should().BeFalse();
        errorsEl.GetArrayLength().Should().BeGreaterThan(0);
    }

    public void Dispose()
    {
        _mockFs.Dispose();
    }
}
