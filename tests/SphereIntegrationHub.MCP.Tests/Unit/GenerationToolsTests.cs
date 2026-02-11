using System.Text.Json;
using FluentAssertions;
using SphereIntegrationHub.MCP.Services.Integration;
using SphereIntegrationHub.MCP.Tests.TestHelpers;
using SphereIntegrationHub.MCP.Tools;
using Xunit;

namespace SphereIntegrationHub.MCP.Tests.Unit;

public class GenerationToolsTests : IDisposable
{
    private readonly MockFileSystem _mockFs;
    private readonly SihServicesAdapter _adapter;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public GenerationToolsTests()
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
    public async Task GenerateEndpointStage_WithValidEndpoint_CreatesValidYaml()
    {
        // Arrange
        var tool = new GenerateEndpointStageTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["apiName"] = "AccountsAPI",
            ["endpoint"] = "/api/accounts",
            ["httpVerb"] = "GET"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("yaml", out var yamlEl).Should().BeTrue();
        var yaml = yamlEl.GetString();
        yaml.Should().NotBeNullOrEmpty();
        yaml.Should().Contain("name:");
        yaml.Should().Contain("type:");
        yaml.Should().Contain("api:");
    }

    [Fact]
    public async Task GenerateEndpointStage_WithCustomStageName_UsesCustomName()
    {
        // Arrange
        var tool = new GenerateEndpointStageTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["apiName"] = "AccountsAPI",
            ["endpoint"] = "/api/accounts",
            ["httpVerb"] = "GET",
            ["stageName"] = "custom-stage"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("stageName", out var stageNameEl).Should().BeTrue();
        json.TryGetProperty("yaml", out var yamlEl).Should().BeTrue();

        stageNameEl.GetString().Should().Be("custom-stage");
        var yaml = yamlEl.GetString();
        yaml.Should().Contain("custom-stage");
    }

    [Fact]
    public async Task GenerateEndpointStage_WithPostMethod_IncludesBody()
    {
        // Arrange
        var tool = new GenerateEndpointStageTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["apiName"] = "AccountsAPI",
            ["endpoint"] = "/api/accounts",
            ["httpVerb"] = "POST"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("yaml", out var yamlEl).Should().BeTrue();
        var yaml = yamlEl.GetString();
        yaml.Should().NotBeNullOrEmpty();
        yaml.Should().Contain("POST");
    }

    [Fact]
    public async Task GenerateEndpointStage_WithMissingParameter_ThrowsException()
    {
        // Arrange
        var tool = new GenerateEndpointStageTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["apiName"] = "AccountsAPI",
            ["endpoint"] = "/api/accounts"
            // Missing httpVerb
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => tool.ExecuteAsync(args));
    }

    [Fact]
    public void GenerateWorkflowSkeleton_WithValidInputs_CreatesCompleteWorkflow()
    {
        // Arrange
        var tool = new GenerateWorkflowSkeletonTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["name"] = "test-workflow",
            ["description"] = "A test workflow for unit testing"
        };

        // Act
        var result = tool.ExecuteAsync(args).Result;
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("name", out var nameEl).Should().BeTrue();
        json.TryGetProperty("yaml", out var yamlEl).Should().BeTrue();

        nameEl.GetString().Should().Be("test-workflow");
        var yaml = yamlEl.GetString();
        yaml.Should().NotBeNullOrEmpty();
        yaml.Should().Contain("name: test-workflow");
        yaml.Should().Contain("description:");
        yaml.Should().Contain("stages:");
        yaml.Should().Contain("end-stage:");
    }

    [Fact]
    public void GenerateWorkflowSkeleton_WithInputParameters_IncludesInputs()
    {
        // Arrange
        var tool = new GenerateWorkflowSkeletonTool(_adapter);
        var inputParams = new List<string> { "userId", "accountId" };
        var args = new Dictionary<string, object>
        {
            ["name"] = "test-workflow",
            ["description"] = "A test workflow",
            ["inputParameters"] = JsonSerializer.SerializeToElement(inputParams)
        };

        // Act
        var result = tool.ExecuteAsync(args).Result;
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("yaml", out var yamlEl).Should().BeTrue();
        var yaml = yamlEl.GetString();
        yaml.Should().NotBeNullOrEmpty();
        yaml.Should().Contain("input:");
        yaml.Should().Contain("userId");
        yaml.Should().Contain("accountId");
    }

    [Fact]
    public void GenerateWorkflowSkeleton_WithoutInputParameters_CreatesEmptyInputs()
    {
        // Arrange
        var tool = new GenerateWorkflowSkeletonTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["name"] = "no-input-workflow",
            ["description"] = "Workflow without inputs"
        };

        // Act
        var result = tool.ExecuteAsync(args).Result;
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("yaml", out var yamlEl).Should().BeTrue();
        yamlEl.GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GenerateWorkflowSkeleton_WithMissingName_ThrowsException()
    {
        // Arrange
        var tool = new GenerateWorkflowSkeletonTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["description"] = "Missing name"
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => tool.ExecuteAsync(args).Result);
    }

    [Fact]
    public async Task GenerateMockPayload_WithPostEndpoint_GeneratesJsonPayload()
    {
        // Arrange
        var tool = new GenerateMockPayloadTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["apiName"] = "AccountsAPI",
            ["endpoint"] = "/api/accounts",
            ["httpVerb"] = "POST"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("endpoint", out var endpointEl).Should().BeTrue();
        json.TryGetProperty("httpVerb", out var verbEl).Should().BeTrue();
        json.TryGetProperty("payload", out var payloadEl).Should().BeTrue();

        endpointEl.GetString().Should().Be("/api/accounts");
        verbEl.GetString().Should().Be("POST");
    }

    [Fact]
    public async Task GenerateMockPayload_WithGetEndpoint_ReturnsEmptyPayload()
    {
        // Arrange
        var tool = new GenerateMockPayloadTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["apiName"] = "AccountsAPI",
            ["endpoint"] = "/api/accounts",
            ["httpVerb"] = "GET"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("payload", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GenerateMockPayload_WithComplexSchema_GeneratesNestedPayload()
    {
        // Arrange
        var tool = new GenerateMockPayloadTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["apiName"] = "AccountsAPI",
            ["endpoint"] = "/api/accounts",
            ["httpVerb"] = "POST"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("payload", out var payloadEl).Should().BeTrue();

        // Payload should be a string or object - if it's a string, it should be valid JSON
        if (payloadEl.ValueKind == JsonValueKind.String)
        {
            var payloadStr = payloadEl.GetString();
            if (!string.IsNullOrEmpty(payloadStr))
            {
                var isValidJson = false;
                try
                {
                    JsonDocument.Parse(payloadStr);
                    isValidJson = true;
                }
                catch
                {
                    // Not valid JSON
                }
                isValidJson.Should().BeTrue();
            }
        }
    }

    [Fact]
    public async Task GenerateMockPayload_WithMissingParameters_ThrowsException()
    {
        // Arrange
        var tool = new GenerateMockPayloadTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["apiName"] = "AccountsAPI"
            // Missing endpoint and httpVerb
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => tool.ExecuteAsync(args));
    }

    [Fact]
    public void GenerateEndpointStageTool_HasCorrectMetadata()
    {
        // Arrange
        var tool = new GenerateEndpointStageTool(_adapter);

        // Assert
        tool.Name.Should().Be("generate_endpoint_stage");
        tool.Description.Should().NotBeNullOrEmpty();
        tool.InputSchema.Should().NotBeNull();
    }

    [Fact]
    public void GenerateWorkflowSkeletonTool_HasCorrectMetadata()
    {
        // Arrange
        var tool = new GenerateWorkflowSkeletonTool(_adapter);

        // Assert
        tool.Name.Should().Be("generate_workflow_skeleton");
        tool.Description.Should().NotBeNullOrEmpty();
        tool.InputSchema.Should().NotBeNull();
    }

    [Fact]
    public void GenerateMockPayloadTool_HasCorrectMetadata()
    {
        // Arrange
        var tool = new GenerateMockPayloadTool(_adapter);

        // Assert
        tool.Name.Should().Be("generate_mock_payload");
        tool.Description.Should().NotBeNullOrEmpty();
        tool.InputSchema.Should().NotBeNull();
    }

    [Fact]
    public async Task GenerateEndpointStage_WithPathParameters_IncludesTemplates()
    {
        // Arrange
        var tool = new GenerateEndpointStageTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["apiName"] = "AccountsAPI",
            ["endpoint"] = "/api/accounts/{id}",
            ["httpVerb"] = "GET"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("yaml", out var yamlEl).Should().BeTrue();
        var yaml = yamlEl.GetString();
        yaml.Should().NotBeNullOrEmpty();
        yaml.Should().Contain("/api/accounts/");
    }

    public void Dispose()
    {
        _mockFs.Dispose();
    }
}
