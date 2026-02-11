using System.Text.Json;
using FluentAssertions;
using SphereIntegrationHub.MCP.Services.Integration;
using SphereIntegrationHub.MCP.Tests.TestHelpers;
using SphereIntegrationHub.MCP.Tools;
using Xunit;

namespace SphereIntegrationHub.MCP.Tests.Unit;

public class ReferenceToolsTests : IDisposable
{
    private readonly MockFileSystem _mockFs;
    private readonly SihServicesAdapter _adapter;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public ReferenceToolsTests()
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
    public async Task ListAvailableWorkflows_WithWorkflowFiles_ReturnsWorkflowList()
    {
        // Arrange
        var workflow1 = TestDataBuilder.CreateSampleWorkflow("workflow1");
        var workflow2 = TestDataBuilder.CreateSampleWorkflow("workflow2");
        _mockFs.AddWorkflow("workflow1.workflow", workflow1);
        _mockFs.AddWorkflow("workflow2.yml", workflow2);

        var tool = new ListAvailableWorkflowsTool(_adapter);

        // Act
        var result = await tool.ExecuteAsync(new Dictionary<string, object>());
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("workflows", out var workflowsEl).Should().BeTrue();
        json.TryGetProperty("count", out var countEl).Should().BeTrue();

        workflowsEl.GetArrayLength().Should().BeGreaterOrEqualTo(2);
        countEl.GetInt32().Should().BeGreaterOrEqualTo(2);
    }

    [Fact]
    public async Task ListAvailableWorkflows_WithOnlyWorkflowExtension_ReturnsWorkflowFiles()
    {
        // Arrange
        var workflow = TestDataBuilder.CreateSampleWorkflow("workflow-only");
        _mockFs.AddWorkflow("workflow-only.workflow", workflow);

        var tool = new ListAvailableWorkflowsTool(_adapter);

        // Act
        var result = await tool.ExecuteAsync(new Dictionary<string, object>());
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("workflows", out var workflowsEl).Should().BeTrue();
        json.TryGetProperty("count", out var countEl).Should().BeTrue();

        workflowsEl.GetArrayLength().Should().Be(1);
        countEl.GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ListAvailableWorkflows_WithEmptyDirectory_ReturnsEmptyList()
    {
        // Arrange
        var tool = new ListAvailableWorkflowsTool(_adapter);

        // Act
        var result = await tool.ExecuteAsync(new Dictionary<string, object>());
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("workflows", out var workflowsEl).Should().BeTrue();
        json.TryGetProperty("count", out var countEl).Should().BeTrue();

        workflowsEl.GetArrayLength().Should().Be(0);
        countEl.GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task ListAvailableWorkflows_WithInvalidYaml_IncludesError()
    {
        // Arrange
        var invalidYaml = "invalid: [yaml - structure";
        _mockFs.AddWorkflow("invalid.yaml", invalidYaml);

        var tool = new ListAvailableWorkflowsTool(_adapter);

        // Act
        var result = await tool.ExecuteAsync(new Dictionary<string, object>());
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("workflows", out var workflowsEl).Should().BeTrue();
        workflowsEl.GetArrayLength().Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task ListAvailableWorkflows_IncludesWorkflowMetadata()
    {
        // Arrange
        var workflow = TestDataBuilder.CreateSampleWorkflow("metadata-test");
        _mockFs.AddWorkflow("metadata-test.yaml", workflow);

        var tool = new ListAvailableWorkflowsTool(_adapter);

        // Act
        var result = await tool.ExecuteAsync(new Dictionary<string, object>());
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("workflows", out var workflowsEl).Should().BeTrue();
        workflowsEl.GetArrayLength().Should().BeGreaterOrEqualTo(1);

        var firstWorkflow = workflowsEl[0];
        firstWorkflow.TryGetProperty("name", out var nameEl).Should().BeTrue();
        nameEl.ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetWorkflowInputsOutputs_WithValidWorkflow_ReturnsInputsAndOutputs()
    {
        // Arrange
        var workflow = TestDataBuilder.CreateSampleWorkflow();
        _mockFs.AddWorkflow("io-test.yaml", workflow);

        var tool = new GetWorkflowInputsOutputsTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = "io-test.yaml"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("workflowName", out var workflowName).Should().BeTrue();
        json.TryGetProperty("inputs", out var inputs).Should().BeTrue();
        json.TryGetProperty("outputs", out var outputs).Should().BeTrue();

        workflowName.ValueKind.Should().NotBe(JsonValueKind.Null);
        inputs.ValueKind.Should().Be(JsonValueKind.Array);
        outputs.ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task GetWorkflowInputsOutputs_WithAbsolutePath_WorksCorrectly()
    {
        // Arrange
        var workflow = TestDataBuilder.CreateSampleWorkflow();
        var absolutePath = Path.Combine(_mockFs.WorkflowsPath, "absolute-io-test.yaml");
        File.WriteAllText(absolutePath, workflow);

        var tool = new GetWorkflowInputsOutputsTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = absolutePath
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("workflowName", out var workflowName).Should().BeTrue();
        workflowName.ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetWorkflowInputsOutputs_WithMissingFile_ThrowsException()
    {
        // Arrange
        var tool = new GetWorkflowInputsOutputsTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = "nonexistent.yaml"
        };

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => tool.ExecuteAsync(args));
    }

    [Fact]
    public async Task GetWorkflowInputsOutputs_WithNoInputs_ReturnsEmptyInputList()
    {
        // Arrange
        var workflowWithoutInputs = @"
name: no-input-workflow
description: Workflow without inputs
version: 1.0.0

stages:
  - name: static-stage
    plugin: HttpClientPlugin
    config:
      url: https://api.example.com/static
      method: GET

outputs:
  - source: {{ context.staticData }}
    target: result
";
        _mockFs.AddWorkflow("no-inputs.yaml", workflowWithoutInputs);

        var tool = new GetWorkflowInputsOutputsTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = "no-inputs.yaml"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("inputs", out var inputs).Should().BeTrue();
        json.TryGetProperty("inputCount", out var inputCount).Should().BeTrue();

        inputs.GetArrayLength().Should().Be(0);
        inputCount.GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task GetWorkflowInputsOutputs_WithComplexInputs_ExtractsAllInputDetails()
    {
        // Arrange
        var workflowWithComplexInputs = @"
name: complex-inputs-workflow
description: Workflow with detailed inputs
version: 1.0.0

input:
  - name: userId
    type: string
    required: true
    description: The user identifier
  - name: limit
    type: integer
    required: false
    description: Maximum number of results

stages:
  - name: test-stage
    plugin: HttpClientPlugin
    config:
      url: https://api.example.com
      method: GET
";
        _mockFs.AddWorkflow("complex-inputs.yaml", workflowWithComplexInputs);

        var tool = new GetWorkflowInputsOutputsTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = "complex-inputs.yaml"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("inputs", out var inputs).Should().BeTrue();
        json.TryGetProperty("inputCount", out var inputCount).Should().BeTrue();

        inputs.GetArrayLength().Should().BeGreaterThan(0);
        inputCount.GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public void ListAvailableWorkflowsTool_HasCorrectMetadata()
    {
        // Arrange
        var tool = new ListAvailableWorkflowsTool(_adapter);

        // Assert
        tool.Name.Should().Be("list_available_workflows");
        tool.Description.Should().NotBeNullOrEmpty();
        tool.InputSchema.Should().NotBeNull();
    }

    [Fact]
    public void GetWorkflowInputsOutputsTool_HasCorrectMetadata()
    {
        // Arrange
        var tool = new GetWorkflowInputsOutputsTool(_adapter);

        // Assert
        tool.Name.Should().Be("get_workflow_inputs_outputs");
        tool.Description.Should().NotBeNullOrEmpty();
        tool.InputSchema.Should().NotBeNull();
    }

    [Fact]
    public async Task ListAvailableWorkflows_IncludesStageCount()
    {
        // Arrange
        var workflow = TestDataBuilder.CreateSampleWorkflow();
        _mockFs.AddWorkflow("stage-count-test.yaml", workflow);

        var tool = new ListAvailableWorkflowsTool(_adapter);

        // Act
        var result = await tool.ExecuteAsync(new Dictionary<string, object>());
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("workflows", out var workflowsEl).Should().BeTrue();
        workflowsEl.GetArrayLength().Should().BeGreaterOrEqualTo(1);
    }

    public void Dispose()
    {
        _mockFs.Dispose();
    }
}
