using System.Text.Json;
using FluentAssertions;
using SphereIntegrationHub.MCP.Services.Integration;
using SphereIntegrationHub.MCP.Tests.TestHelpers;
using SphereIntegrationHub.MCP.Tools;
using Xunit;

namespace SphereIntegrationHub.MCP.Tests.Unit;

public class CatalogToolsTests : IDisposable
{
    private readonly MockFileSystem _mockFs;
    private readonly SihServicesAdapter _adapter;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public CatalogToolsTests()
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
    public async Task ListApiCatalogVersions_WithCachedVersions_ReturnsVersionList()
    {
        // Arrange
        var tool = new ListApiCatalogVersionsTool(_adapter);

        // Act
        var result = await tool.ExecuteAsync(new Dictionary<string, object>());
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("versions", out var versionsEl).Should().BeTrue();
        json.TryGetProperty("count", out var countEl).Should().BeTrue();

        var versions = versionsEl.EnumerateArray().Select(v => v.GetString()).ToList();
        versions.Should().HaveCountGreaterOrEqualTo(2);
        versions.Should().Contain("3.10");
        versions.Should().Contain("3.11");
        countEl.GetInt32().Should().BeGreaterOrEqualTo(2);
    }

    [Fact]
    public async Task ListApiCatalogVersions_WithEmptyCache_ReturnsEmptyList()
    {
        // Arrange - Create filesystem with empty API catalog (no versions)
        using var emptyFs = new MockFileSystem();
        emptyFs.AddApiCatalog("[]"); // Empty array - no versions
        var emptyAdapter = new SihServicesAdapter(emptyFs.RootPath);
        var tool = new ListApiCatalogVersionsTool(emptyAdapter);

        // Act
        var result = await tool.ExecuteAsync(new Dictionary<string, object>());
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("versions", out var versionsEl).Should().BeTrue();
        json.TryGetProperty("count", out var countEl).Should().BeTrue();

        versionsEl.GetArrayLength().Should().Be(0);
        countEl.GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task GetApiDefinitions_WithValidVersion_ReturnsApiList()
    {
        // Arrange
        var tool = new GetApiDefinitionsTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("version", out var versionEl).Should().BeTrue();
        json.TryGetProperty("apis", out var apisEl).Should().BeTrue();
        json.TryGetProperty("count", out var countEl).Should().BeTrue();

        versionEl.GetString().Should().Be("3.10");
        apisEl.GetArrayLength().Should().BeGreaterThan(0);
        countEl.GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetApiDefinitions_WithMissingVersion_ThrowsException()
    {
        // Arrange
        var tool = new GetApiDefinitionsTool(_adapter);
        var args = new Dictionary<string, object>();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => tool.ExecuteAsync(args));
    }

    [Fact]
    public async Task GetApiEndpoints_WithValidApiName_ReturnsEndpointList()
    {
        // Arrange
        var tool = new GetApiEndpointsTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["apiName"] = "AccountsAPI"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("version", out var versionEl).Should().BeTrue();
        json.TryGetProperty("apiName", out var apiNameEl).Should().BeTrue();
        json.TryGetProperty("endpoints", out var endpointsEl).Should().BeTrue();
        json.TryGetProperty("count", out var countEl).Should().BeTrue();

        versionEl.GetString().Should().Be("3.10");
        apiNameEl.GetString().Should().Be("AccountsAPI");
        endpointsEl.GetArrayLength().Should().BeGreaterThan(0);
        countEl.GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetApiEndpoints_WithMissingApiName_ThrowsException()
    {
        // Arrange
        var tool = new GetApiEndpointsTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10"
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => tool.ExecuteAsync(args));
    }

    [Fact]
    public async Task GetEndpointSchema_WithValidEndpoint_ReturnsSchemaDetails()
    {
        // Arrange
        var tool = new GetEndpointSchemaTool(_adapter);
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

        endpointEl.GetString().Should().Be("/api/accounts");
        verbEl.GetString().Should().Be("POST");
    }

    [Fact]
    public async Task GetEndpointSchema_WithInvalidEndpoint_ThrowsException()
    {
        // Arrange
        var tool = new GetEndpointSchemaTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["apiName"] = "AccountsAPI",
            ["endpoint"] = "/api/nonexistent",
            ["httpVerb"] = "GET"
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => tool.ExecuteAsync(args));
    }

    [Fact]
    public async Task GetEndpointSchema_WithMissingRequiredParameter_ThrowsException()
    {
        // Arrange
        var tool = new GetEndpointSchemaTool(_adapter);
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
    public void ListApiCatalogVersionsTool_HasCorrectMetadata()
    {
        // Arrange
        var tool = new ListApiCatalogVersionsTool(_adapter);

        // Assert
        tool.Name.Should().Be("list_api_catalog_versions");
        tool.Description.Should().NotBeNullOrEmpty();
        tool.InputSchema.Should().NotBeNull();
    }

    [Fact]
    public void GetApiDefinitionsTool_HasCorrectMetadata()
    {
        // Arrange
        var tool = new GetApiDefinitionsTool(_adapter);

        // Assert
        tool.Name.Should().Be("get_api_definitions");
        tool.Description.Should().NotBeNullOrEmpty();
        tool.InputSchema.Should().NotBeNull();
    }

    [Fact]
    public void GetApiEndpointsTool_HasCorrectMetadata()
    {
        // Arrange
        var tool = new GetApiEndpointsTool(_adapter);

        // Assert
        tool.Name.Should().Be("get_api_endpoints");
        tool.Description.Should().NotBeNullOrEmpty();
        tool.InputSchema.Should().NotBeNull();
    }

    [Fact]
    public void GetEndpointSchemaTool_HasCorrectMetadata()
    {
        // Arrange
        var tool = new GetEndpointSchemaTool(_adapter);

        // Assert
        tool.Name.Should().Be("get_endpoint_schema");
        tool.Description.Should().NotBeNullOrEmpty();
        tool.InputSchema.Should().NotBeNull();
    }

    public void Dispose()
    {
        _mockFs.Dispose();
    }
}
