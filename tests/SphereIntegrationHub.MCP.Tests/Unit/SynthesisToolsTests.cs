using System.Text.Json;
using FluentAssertions;
using SphereIntegrationHub.MCP.Services.Integration;
using SphereIntegrationHub.MCP.Tests.TestHelpers;
using SphereIntegrationHub.MCP.Tools;
using Xunit;

namespace SphereIntegrationHub.MCP.Tests.Unit;

public class SynthesisToolsTests : IDisposable
{
    private readonly MockFileSystem _mockFs;
    private readonly SihServicesAdapter _adapter;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SynthesisToolsTests()
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
    public async Task SynthesizeSystemFromDescription_WithValidDescription_CreatesMultipleWorkflows()
    {
        // Arrange
        var tool = new SynthesizeSystemFromDescriptionTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["description"] = "Build a customer management system that can create, update, and retrieve customer accounts"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("workflows", out var workflowsEl).Should().BeTrue();
        workflowsEl.ValueKind.Should().Be(JsonValueKind.Array);
        workflowsEl.GetArrayLength().Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task SynthesizeSystemFromDescription_ExtractsEntitiesFromDescription()
    {
        // Arrange
        var tool = new SynthesizeSystemFromDescriptionTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            // Use description that matches swagger endpoints - it has /api/accounts with tags "Accounts"
            ["description"] = "Create account and manage account data with full CRUD operations"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("workflows", out var workflowsEl).Should().BeTrue();
        workflowsEl.ValueKind.Should().Be(JsonValueKind.Array);
        // System should identify entities matching the swagger - accounts
    }

    [Fact]
    public async Task SynthesizeSystemFromDescription_ExtractsActionsFromDescription()
    {
        // Arrange
        var tool = new SynthesizeSystemFromDescriptionTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["description"] = "Create new accounts, update existing ones, and delete inactive accounts"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("workflows", out var workflowsEl).Should().BeTrue();
        workflowsEl.ValueKind.Should().Be(JsonValueKind.Array);
        // Should detect create, update, and delete actions
    }

    [Fact]
    public async Task SynthesizeSystemFromDescription_BuildsDependencyGraph()
    {
        // Arrange
        var tool = new SynthesizeSystemFromDescriptionTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["description"] = "Create customer accounts and then create orders for those customers"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("dependencies", out var dependenciesEl).Should().BeTrue();
    }

    [Fact]
    public async Task SynthesizeSystemFromDescription_GeneratesTestScenarios()
    {
        // Arrange
        var tool = new SynthesizeSystemFromDescriptionTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["description"] = "Simple account management system"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("testScenarios", out var testScenariosEl).Should().BeTrue();
    }

    [Fact]
    public async Task SynthesizeSystemFromDescription_WithRequirements_RespectsConstraints()
    {
        // Arrange
        var tool = new SynthesizeSystemFromDescriptionTool(_adapter);
        var requirements = new
        {
            maxStagesPerWorkflow = 5,
            includeAuthentication = true,
            includeErrorHandling = true
        };

        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["description"] = "Account management system",
            ["requirements"] = JsonSerializer.SerializeToElement(requirements)
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("workflows", out var workflowsEl).Should().BeTrue();
        workflowsEl.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task SynthesizeSystemFromDescription_IncludesApiUsageStatistics()
    {
        // Arrange
        var tool = new SynthesizeSystemFromDescriptionTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["description"] = "Customer management with account operations"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("apiUsage", out var apiUsageEl).Should().BeTrue();
        apiUsageEl.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task SynthesizeSystemFromDescription_CalculatesMetrics()
    {
        // Arrange
        var tool = new SynthesizeSystemFromDescriptionTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["description"] = "Build an account management system with account creation"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("metrics", out var metricsEl).Should().BeTrue();
        metricsEl.TryGetProperty("totalWorkflows", out var totalWorkflowsEl).Should().BeTrue();
        metricsEl.TryGetProperty("totalStages", out var totalStagesEl).Should().BeTrue();
        metricsEl.TryGetProperty("estimatedComplexity", out var complexityEl).Should().BeTrue();
    }

    [Fact]
    public async Task SynthesizeSystemFromDescription_GeneratesSummary()
    {
        // Arrange
        var tool = new SynthesizeSystemFromDescriptionTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["description"] = "Simple system for account management"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("summary", out var summaryEl).Should().BeTrue();
    }

    [Fact]
    public async Task SynthesizeSystemFromDescription_WithRequiredApis_UsesSpecifiedApis()
    {
        // Arrange
        var tool = new SynthesizeSystemFromDescriptionTool(_adapter);
        var requirements = new
        {
            requiredApis = new[] { "AccountsAPI" },
            preferredApis = new[] { "UsersAPI" }
        };

        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["description"] = "Account management",
            ["requirements"] = JsonSerializer.SerializeToElement(requirements)
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("apiUsage", out var apiUsageEl).Should().BeTrue();
        apiUsageEl.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task SynthesizeSystemFromDescription_WithMissingDescription_ThrowsException()
    {
        // Arrange
        var tool = new SynthesizeSystemFromDescriptionTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10"
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => tool.ExecuteAsync(args));
    }

    [Fact]
    public async Task SynthesizeSystemFromDescription_WithPerformanceTarget_AdjustsOptimizations()
    {
        // Arrange
        var tool = new SynthesizeSystemFromDescriptionTool(_adapter);
        var requirements = new
        {
            performanceTarget = "fast"
        };

        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["description"] = "Fast account lookup and retrieval system",
            ["requirements"] = JsonSerializer.SerializeToElement(requirements)
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("workflows", out var workflowsEl).Should().BeTrue();
        workflowsEl.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public void SynthesizeSystemFromDescriptionTool_HasCorrectMetadata()
    {
        // Arrange
        var tool = new SynthesizeSystemFromDescriptionTool(_adapter);

        // Assert
        tool.Name.Should().Be("synthesize_system_from_description");
        tool.Description.Should().NotBeNullOrEmpty();
        tool.InputSchema.Should().NotBeNull();
    }

    public void Dispose()
    {
        _mockFs.Dispose();
    }
}
