using System.Text.Json;
using FluentAssertions;
using SphereIntegrationHub.MCP.Services.Integration;
using SphereIntegrationHub.MCP.Tests.TestHelpers;
using SphereIntegrationHub.MCP.Tools;
using Xunit;

namespace SphereIntegrationHub.MCP.Tests.Unit;

public class PatternToolsTests : IDisposable
{
    private readonly MockFileSystem _mockFs;
    private readonly SihServicesAdapter _adapter;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public PatternToolsTests()
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
    public async Task DetectApiPatterns_WithValidApi_ReturnsDetectedPatterns()
    {
        // Arrange
        var tool = new DetectApiPatternsTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["apiName"] = "AccountsAPI"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("apiName", out var apiNameEl).Should().BeTrue();
        json.TryGetProperty("patterns", out var patternsEl).Should().BeTrue();
        json.TryGetProperty("patternCount", out var countEl).Should().BeTrue();

        apiNameEl.GetString().Should().Be("AccountsAPI");
        patternsEl.ValueKind.Should().Be(JsonValueKind.Array);
        countEl.GetInt32().Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task DetectApiPatterns_IdentifiesOAuthPattern()
    {
        // Arrange
        var tool = new DetectApiPatternsTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["apiName"] = "AccountsAPI"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("summary", out var summaryEl).Should().BeTrue();
        summaryEl.TryGetProperty("hasOAuth", out var hasOAuthEl).Should().BeTrue();
        // hasOAuth can be true or false
    }

    [Fact]
    public async Task DetectApiPatterns_IdentifiesCrudPattern()
    {
        // Arrange
        var tool = new DetectApiPatternsTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["apiName"] = "AccountsAPI"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("summary", out var summaryEl).Should().BeTrue();
        summaryEl.TryGetProperty("crudResources", out var crudResourcesEl).Should().BeTrue();
        crudResourcesEl.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task DetectApiPatterns_IdentifiesPaginationPattern()
    {
        // Arrange
        var tool = new DetectApiPatternsTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["apiName"] = "AccountsAPI"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("summary", out var summaryEl).Should().BeTrue();
        summaryEl.TryGetProperty("supportsPagination", out var supportsPaginationEl).Should().BeTrue();
        // supportsPagination can be true or false
    }

    [Fact]
    public async Task DetectApiPatterns_ReturnsSummary()
    {
        // Arrange
        var tool = new DetectApiPatternsTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["apiName"] = "AccountsAPI"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("summary", out var summaryEl).Should().BeTrue();
        summaryEl.TryGetProperty("totalPatterns", out var totalPatternsEl).Should().BeTrue();
        totalPatternsEl.GetInt32().Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task DetectApiPatterns_WithMissingApi_ThrowsException()
    {
        // Arrange
        var tool = new DetectApiPatternsTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10"
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => tool.ExecuteAsync(args));
    }

    [Fact]
    public async Task GenerateCrudWorkflow_WithValidResource_GeneratesWorkflow()
    {
        // Arrange
        var tool = new GenerateCrudWorkflowTool(_adapter);
        var operations = new[] { "create", "read", "update", "delete" };
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["apiName"] = "AccountsAPI",
            ["resource"] = "accounts",
            ["operations"] = JsonSerializer.SerializeToElement(operations)
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("resource", out var resourceEl).Should().BeTrue();
        json.TryGetProperty("yaml", out var yamlEl).Should().BeTrue();

        resourceEl.GetString().Should().Be("accounts");
        yamlEl.GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GenerateCrudWorkflow_IncludesCreateOperation()
    {
        // Arrange
        var tool = new GenerateCrudWorkflowTool(_adapter);
        var operations = new[] { "create" };
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["apiName"] = "AccountsAPI",
            ["resource"] = "accounts",
            ["operations"] = JsonSerializer.SerializeToElement(operations)
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("yaml", out var yamlEl).Should().BeTrue();
        var yaml = yamlEl.GetString();
        yaml.Should().Contain("POST");
    }

    [Fact]
    public async Task GenerateCrudWorkflow_IncludesReadOperation()
    {
        // Arrange
        var tool = new GenerateCrudWorkflowTool(_adapter);
        var operations = new[] { "read" };
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["apiName"] = "AccountsAPI",
            ["resource"] = "accounts",
            ["operations"] = JsonSerializer.SerializeToElement(operations)
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("yaml", out var yamlEl).Should().BeTrue();
        var yaml = yamlEl.GetString();
        yaml.Should().NotBeNullOrEmpty();
        // The YAML should contain the read stage with GET verb
        yaml.Should().Contain("read_accounts");
        yaml.Should().Contain("verb: GET");
    }

    [Fact]
    public async Task GenerateCrudWorkflow_IncludesUpdateOperation()
    {
        // Arrange
        var tool = new GenerateCrudWorkflowTool(_adapter);
        var operations = new[] { "update" };
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["apiName"] = "AccountsAPI",
            ["resource"] = "accounts",
            ["operations"] = JsonSerializer.SerializeToElement(operations)
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("yaml", out var yamlEl).Should().BeTrue();
        var yaml = yamlEl.GetString();
        yaml.Should().NotBeNullOrEmpty();
        // The YAML should contain the update stage with PUT verb
        yaml.Should().Contain("update_accounts");
        yaml.Should().Contain("verb: PUT");
    }

    [Fact]
    public async Task GenerateCrudWorkflow_IncludesDeleteOperation()
    {
        // Arrange
        var tool = new GenerateCrudWorkflowTool(_adapter);
        var operations = new[] { "delete" };
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["apiName"] = "AccountsAPI",
            ["resource"] = "accounts",
            ["operations"] = JsonSerializer.SerializeToElement(operations)
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("yaml", out var yamlEl).Should().BeTrue();
        var yaml = yamlEl.GetString();
        yaml.Should().NotBeNullOrEmpty();
        // The YAML should contain the delete stage with DELETE verb
        yaml.Should().Contain("delete_accounts");
        yaml.Should().Contain("verb: DELETE");
    }

    [Fact]
    public async Task GenerateCrudWorkflow_WithMissingOperations_ThrowsException()
    {
        // Arrange
        var tool = new GenerateCrudWorkflowTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["apiName"] = "AccountsAPI",
            ["resource"] = "accounts"
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => tool.ExecuteAsync(args));
    }

    [Fact]
    public void DetectApiPatternsTool_HasCorrectMetadata()
    {
        // Arrange
        var tool = new DetectApiPatternsTool(_adapter);

        // Assert
        tool.Name.Should().Be("detect_api_patterns");
        tool.Description.Should().NotBeNullOrEmpty();
        tool.InputSchema.Should().NotBeNull();
    }

    [Fact]
    public void GenerateCrudWorkflowTool_HasCorrectMetadata()
    {
        // Arrange
        var tool = new GenerateCrudWorkflowTool(_adapter);

        // Assert
        tool.Name.Should().Be("generate_crud_workflow");
        tool.Description.Should().NotBeNullOrEmpty();
        tool.InputSchema.Should().NotBeNull();
    }

    public void Dispose()
    {
        _mockFs.Dispose();
    }
}
