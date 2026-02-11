using System.Text.Json;
using FluentAssertions;
using SphereIntegrationHub.MCP.Services.Integration;
using SphereIntegrationHub.MCP.Tests.TestHelpers;
using SphereIntegrationHub.MCP.Tools;
using Xunit;

namespace SphereIntegrationHub.MCP.Tests.Unit;

public class DiagnosticToolsTests : IDisposable
{
    private readonly MockFileSystem _mockFs;
    private readonly SihServicesAdapter _adapter;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public DiagnosticToolsTests()
    {
        _mockFs = new MockFileSystem();
        _mockFs.AddApiCatalog(TestDataBuilder.CreateSampleApiCatalog());
        _adapter = new SihServicesAdapter(_mockFs.RootPath);
    }

    private static JsonElement ToJson(object result)
    {
        var json = JsonSerializer.Serialize(result, JsonOpts);
        return JsonDocument.Parse(json).RootElement;
    }

    [Fact]
    public async Task ExplainValidationError_WithStageError_ProvidesDetailedExplanation()
    {
        // Arrange
        var tool = new ExplainValidationErrorTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["errorCategory"] = "Stage",
            ["errorMessage"] = "name is required"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("category", out var category).Should().BeTrue();
        json.TryGetProperty("explanation", out var explanation).Should().BeTrue();
        json.TryGetProperty("suggestions", out var suggestions).Should().BeTrue();

        category.GetString().Should().Be("Stage");
        explanation.ValueKind.Should().NotBe(JsonValueKind.Null);
        suggestions.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task ExplainValidationError_WithWorkflowError_ProvidesSuggestions()
    {
        // Arrange
        var tool = new ExplainValidationErrorTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["errorCategory"] = "Workflow",
            ["errorMessage"] = "version is required"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("suggestions", out var suggestions).Should().BeTrue();
        suggestions.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExplainValidationError_WithParseError_ProvidesYamlTips()
    {
        // Arrange
        var tool = new ExplainValidationErrorTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["errorCategory"] = "Parse",
            ["errorMessage"] = "YAML syntax error at line 5"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("suggestions", out var suggestions).Should().BeTrue();

        var suggestionsList = new List<string>();
        foreach (var item in suggestions.EnumerateArray())
        {
            suggestionsList.Add(item.GetString() ?? "");
        }

        suggestionsList.Should().Contain(s => s.Contains("YAML", StringComparison.OrdinalIgnoreCase) || s.Contains("indentation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExplainValidationError_IncludesExamples()
    {
        // Arrange
        var tool = new ExplainValidationErrorTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["errorCategory"] = "Stage",
            ["errorMessage"] = "name is required"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("examples", out var examples).Should().BeTrue();
        examples.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task ExplainValidationError_IncludesRelatedDocs()
    {
        // Arrange
        var tool = new ExplainValidationErrorTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["errorCategory"] = "Stage",
            ["errorMessage"] = "type is required"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("relatedDocs", out var relatedDocs).Should().BeTrue();
        relatedDocs.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExplainValidationError_WithMissingCategory_ThrowsException()
    {
        // Arrange
        var tool = new ExplainValidationErrorTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["errorMessage"] = "some error"
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => tool.ExecuteAsync(args));
    }

    [Fact]
    public async Task GetPluginCapabilities_WithoutFilter_ReturnsAllStageTypes()
    {
        // Arrange
        var tool = new GetPluginCapabilitiesTool(_adapter);

        // Act
        var result = await tool.ExecuteAsync(new Dictionary<string, object>());
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("stageTypes", out var stageTypes).Should().BeTrue();
        stageTypes.ValueKind.Should().Be(JsonValueKind.Array);
        stageTypes.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetPluginCapabilities_WithSpecificPlugin_ReturnsOnlyThatPlugin()
    {
        // Arrange
        var tool = new GetPluginCapabilitiesTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["pluginType"] = "api"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("type", out var type).Should().BeTrue();
        json.TryGetProperty("description", out var description).Should().BeTrue();

        type.GetString().Should().Be("api");
        description.ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetPluginCapabilities_IncludesRequiredAndOptionalFields()
    {
        // Arrange
        var tool = new GetPluginCapabilitiesTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["pluginType"] = "api"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("requiredFields", out var requiredFields).Should().BeTrue();
        json.TryGetProperty("optionalFields", out var optionalFields).Should().BeTrue();

        requiredFields.ValueKind.Should().Be(JsonValueKind.Array);
        optionalFields.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetPluginCapabilities_IncludesCapabilitiesList()
    {
        // Arrange
        var tool = new GetPluginCapabilitiesTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["pluginType"] = "transform"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("capabilities", out var capabilities).Should().BeTrue();
        capabilities.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task SuggestResilienceConfig_ForApiStage_SuggestsRetryAndTimeout()
    {
        // Arrange
        var tool = new SuggestResilienceConfigTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["stageType"] = "api",
            ["operation"] = "GET /api/users",
            ["critical"] = false
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("recommendations", out var recommendations).Should().BeTrue();
        recommendations.TryGetProperty("retry", out var retry).Should().BeTrue();
        recommendations.TryGetProperty("timeout", out var timeout).Should().BeTrue();

        retry.ValueKind.Should().NotBe(JsonValueKind.Null);
        timeout.ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task SuggestResilienceConfig_ForReadOperation_EnablesRetry()
    {
        // Arrange
        var tool = new SuggestResilienceConfigTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["stageType"] = "api",
            ["operation"] = "GET /api/data",
            ["critical"] = false
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("recommendations", out var recommendations).Should().BeTrue();
        recommendations.TryGetProperty("retry", out var retry).Should().BeTrue();
        retry.TryGetProperty("enabled", out var enabled).Should().BeTrue();

        enabled.GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task SuggestResilienceConfig_ForCriticalOperation_IncreasesRetries()
    {
        // Arrange
        var tool = new SuggestResilienceConfigTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["stageType"] = "api",
            ["operation"] = "GET /api/critical-data",
            ["critical"] = true
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("recommendations", out var recommendations).Should().BeTrue();
        recommendations.TryGetProperty("retry", out var retry).Should().BeTrue();
        retry.TryGetProperty("maxRetries", out var maxRetries).Should().BeTrue();

        maxRetries.GetInt32().Should().BeGreaterThan(3);
    }

    [Fact]
    public async Task SuggestResilienceConfig_ForCriticalOperation_SuggestsCircuitBreaker()
    {
        // Arrange
        var tool = new SuggestResilienceConfigTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["stageType"] = "api",
            ["operation"] = "POST /api/important",
            ["critical"] = true
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("recommendations", out var recommendations).Should().BeTrue();
        recommendations.TryGetProperty("circuitBreaker", out var circuitBreaker).Should().BeTrue();
        circuitBreaker.TryGetProperty("enabled", out var enabled).Should().BeTrue();

        enabled.GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task SuggestResilienceConfig_IncludesConfigurationExample()
    {
        // Arrange
        var tool = new SuggestResilienceConfigTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["stageType"] = "api",
            ["operation"] = "GET /api/test"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("example", out var example).Should().BeTrue();
        example.ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task SuggestResilienceConfig_WithMissingStageType_ThrowsException()
    {
        // Arrange
        var tool = new SuggestResilienceConfigTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["operation"] = "GET /api/test"
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => tool.ExecuteAsync(args));
    }

    [Fact]
    public void ExplainValidationErrorTool_HasCorrectMetadata()
    {
        // Arrange
        var tool = new ExplainValidationErrorTool(_adapter);

        // Assert
        tool.Name.Should().Be("explain_validation_error");
        tool.Description.Should().NotBeNullOrEmpty();
        tool.InputSchema.Should().NotBeNull();
    }

    [Fact]
    public void GetPluginCapabilitiesTool_HasCorrectMetadata()
    {
        // Arrange
        var tool = new GetPluginCapabilitiesTool(_adapter);

        // Assert
        tool.Name.Should().Be("get_plugin_capabilities");
        tool.Description.Should().NotBeNullOrEmpty();
        tool.InputSchema.Should().NotBeNull();
    }

    [Fact]
    public void SuggestResilienceConfigTool_HasCorrectMetadata()
    {
        // Arrange
        var tool = new SuggestResilienceConfigTool(_adapter);

        // Assert
        tool.Name.Should().Be("suggest_resilience_config");
        tool.Description.Should().NotBeNullOrEmpty();
        tool.InputSchema.Should().NotBeNull();
    }

    public void Dispose()
    {
        _mockFs.Dispose();
    }
}
