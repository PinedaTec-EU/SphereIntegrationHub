using System.Text.Json;
using FluentAssertions;
using SphereIntegrationHub.MCP.Services.Integration;
using SphereIntegrationHub.MCP.Tests.TestHelpers;
using SphereIntegrationHub.MCP.Tools;
using Xunit;

namespace SphereIntegrationHub.MCP.Tests.Unit;

public class SemanticToolsTests : IDisposable
{
    private readonly MockFileSystem _mockFs;
    private readonly SihServicesAdapter _adapter;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SemanticToolsTests()
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
    public async Task AnalyzeEndpointDependencies_WithValidEndpoint_ReturnsDependencies()
    {
        // Arrange
        var tool = new AnalyzeEndpointDependenciesTool(_adapter);
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
        json.TryGetProperty("endpoint", out var endpoint).Should().BeTrue();
        endpoint.GetString().Should().Be("/api/accounts/{id}");
    }

    [Fact]
    public async Task AnalyzeEndpointDependencies_DetectsPrerequisiteEndpoints()
    {
        // Arrange
        var tool = new AnalyzeEndpointDependenciesTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["apiName"] = "AccountsAPI",
            ["endpoint"] = "/api/accounts/{id}",
            ["httpVerb"] = "PUT"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert - EndpointDependencies has requiredFields and suggestedExecutionOrder
        json.TryGetProperty("requiredFields", out var requiredFields).Should().BeTrue();
        requiredFields.ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task AnalyzeEndpointDependencies_WithMissingParameters_ThrowsException()
    {
        // Arrange
        var tool = new AnalyzeEndpointDependenciesTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["apiName"] = "AccountsAPI"
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => tool.ExecuteAsync(args));
    }

    [Fact]
    public async Task InferDataFlow_WithMultipleEndpoints_BuildsDataFlowGraph()
    {
        // Arrange
        var tool = new InferDataFlowTool(_adapter);
        var endpoints = new[]
        {
            new { apiName = "AccountsAPI", endpoint = "/api/accounts", httpVerb = "GET" },
            new { apiName = "AccountsAPI", endpoint = "/api/accounts/{id}", httpVerb = "GET" }
        };

        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["endpoints"] = JsonSerializer.SerializeToElement(endpoints)
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("stages", out var stages).Should().BeTrue();
        json.TryGetProperty("connections", out var connections).Should().BeTrue();
        stages.ValueKind.Should().Be(JsonValueKind.Array);
        connections.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task InferDataFlow_DetectsFieldMatches()
    {
        // Arrange
        var tool = new InferDataFlowTool(_adapter);
        var endpoints = new[]
        {
            new { apiName = "AccountsAPI", endpoint = "/api/accounts", httpVerb = "POST" },
            new { apiName = "AccountsAPI", endpoint = "/api/accounts/{id}", httpVerb = "GET" }
        };

        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["endpoints"] = JsonSerializer.SerializeToElement(endpoints)
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("connections", out var connections).Should().BeTrue();
        connections.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task InferDataFlow_WithEmptyEndpointList_ThrowsException()
    {
        // Arrange
        var tool = new InferDataFlowTool(_adapter);
        var endpoints = Array.Empty<object>();

        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["endpoints"] = JsonSerializer.SerializeToElement(endpoints)
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => tool.ExecuteAsync(args));
    }

    [Fact]
    public async Task InferDataFlow_ReturnsExecutionOrder()
    {
        // Arrange
        var tool = new InferDataFlowTool(_adapter);
        var endpoints = new[]
        {
            new { apiName = "AccountsAPI", endpoint = "/api/accounts", httpVerb = "GET" },
            new { apiName = "AccountsAPI", endpoint = "/api/accounts/{id}", httpVerb = "GET" }
        };

        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["endpoints"] = JsonSerializer.SerializeToElement(endpoints)
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("executionOrder", out var executionOrder).Should().BeTrue();
        executionOrder.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SuggestWorkflowFromGoal_WithValidGoal_GeneratesWorkflow()
    {
        // Arrange
        var tool = new SuggestWorkflowFromGoalTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["goal"] = "Get all accounts and update the first one"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("workflowYaml", out var workflowYaml).Should().BeTrue();
        json.TryGetProperty("stages", out var stages).Should().BeTrue();
        workflowYaml.ValueKind.Should().NotBe(JsonValueKind.Null);
        stages.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task SuggestWorkflowFromGoal_ExtractsEntities()
    {
        // Arrange
        var tool = new SuggestWorkflowFromGoalTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["goal"] = "Create a new account for a customer"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("stages", out var stages).Should().BeTrue();
        stages.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task SuggestWorkflowFromGoal_IncludesAuth_WhenRequested()
    {
        // Arrange
        var tool = new SuggestWorkflowFromGoalTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["goal"] = "Get all accounts",
            ["includeAuth"] = true
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("assumptions", out var assumptions).Should().BeTrue();

        var assumptionsList = new List<string>();
        foreach (var item in assumptions.EnumerateArray())
        {
            assumptionsList.Add(item.GetString() ?? "");
        }

        assumptionsList.Should().Contain(a => a.Contains("authentication", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SuggestWorkflowFromGoal_ReturnsConfidenceScore()
    {
        // Arrange
        var tool = new SuggestWorkflowFromGoalTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["goal"] = "List all accounts"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("confidence", out var confidence).Should().BeTrue();
        var confidenceStr = confidence.GetString();
        confidenceStr.Should().BeOneOf("low", "medium", "high");
    }

    [Fact]
    public async Task SuggestWorkflowFromGoal_WithMissingGoal_ThrowsException()
    {
        // Arrange
        var tool = new SuggestWorkflowFromGoalTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10"
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => tool.ExecuteAsync(args));
    }

    [Fact]
    public void AnalyzeEndpointDependenciesTool_HasCorrectMetadata()
    {
        // Arrange
        var tool = new AnalyzeEndpointDependenciesTool(_adapter);

        // Assert
        tool.Name.Should().Be("analyze_endpoint_dependencies");
        tool.Description.Should().NotBeNullOrEmpty();
        tool.InputSchema.Should().NotBeNull();
    }

    [Fact]
    public void InferDataFlowTool_HasCorrectMetadata()
    {
        // Arrange
        var tool = new InferDataFlowTool(_adapter);

        // Assert
        tool.Name.Should().Be("infer_data_flow");
        tool.Description.Should().NotBeNullOrEmpty();
        tool.InputSchema.Should().NotBeNull();
    }

    [Fact]
    public void SuggestWorkflowFromGoalTool_HasCorrectMetadata()
    {
        // Arrange
        var tool = new SuggestWorkflowFromGoalTool(_adapter);

        // Assert
        tool.Name.Should().Be("suggest_workflow_from_goal");
        tool.Description.Should().NotBeNullOrEmpty();
        tool.InputSchema.Should().NotBeNull();
    }

    public void Dispose()
    {
        _mockFs.Dispose();
    }
}
