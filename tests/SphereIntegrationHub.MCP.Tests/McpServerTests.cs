using System.Text.Json;
using SphereIntegrationHub.MCP.Core;
using SphereIntegrationHub.MCP.Services.Integration;
using Xunit;

namespace SphereIntegrationHub.MCP.Tests;

public class McpServerTests
{
    private readonly string _projectRoot;
    private readonly SihServicesAdapter _adapter;
    private readonly JsonSerializerOptions _jsonOptions;

    public McpServerTests()
    {
        // Use the actual project root for testing
        _projectRoot = Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", "..", ".."
        ));

        _adapter = new SihServicesAdapter(_projectRoot);
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    [Fact]
    public void McpServer_CanBeCreated()
    {
        var server = new McpServer(_adapter, _jsonOptions);
        Assert.NotNull(server);
    }

    [Fact]
    public async Task McpServer_HandlesInitialize()
    {
        var server = new McpServer(_adapter, _jsonOptions);

        var request = new McpRequest
        {
            Id = 1,
            Method = "initialize",
            Params = new Dictionary<string, object>()
        };

        // Act
        var response = await server.ProcessRequestAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.Equal(1, response.Id);
        Assert.Null(response.Error);
    }

    [Fact]
    public async Task McpServer_HandlesToolsList()
    {
        var server = new McpServer(_adapter, _jsonOptions);

        var request = new McpRequest
        {
            Id = 2,
            Method = "tools/list",
            Params = new Dictionary<string, object>()
        };

        // Act
        var response = await server.ProcessRequestAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.Null(response.Error);
        Assert.NotNull(response.Result);
    }
}
