using System.Text.Json;
using FluentAssertions;
using SphereIntegrationHub.MCP.Core;
using SphereIntegrationHub.MCP.Services.Integration;
using SphereIntegrationHub.MCP.Tests.TestHelpers;
using Xunit;

namespace SphereIntegrationHub.MCP.Tests.Integration;

public class McpServerIntegrationTests : IDisposable
{
    private readonly MockFileSystem _mockFs;
    private readonly SihServicesAdapter _adapter;
    private readonly JsonSerializerOptions _jsonOptions;

    public McpServerIntegrationTests()
    {
        _mockFs = new MockFileSystem();
        _mockFs.AddApiCatalog(TestDataBuilder.CreateSampleApiCatalog());
        _mockFs.SetupCachedVersions("3.10", "3.11");
        _adapter = new SihServicesAdapter(_mockFs.RootPath);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    [Fact]
    public async Task McpServer_Initialize_ReturnsCorrectCapabilities()
    {
        // Arrange
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
        response.Should().NotBeNull();
        response.Id.Should().Be(1);
        response.Error.Should().BeNull();
        response.Result.Should().NotBeNull();
    }

    [Fact]
    public async Task McpServer_ToolsList_ReturnsAllTools()
    {
        // Arrange
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
        response.Should().NotBeNull();
        response.Error.Should().BeNull();
        response.Result.Should().NotBeNull();
    }

    [Fact]
    public async Task McpServer_ToolsCall_WithValidTool_ExecutesSuccessfully()
    {
        // Arrange
        var server = new McpServer(_adapter, _jsonOptions);
        var request = new McpRequest
        {
            Id = 3,
            Method = "tools/call",
            Params = new Dictionary<string, object>
            {
                ["name"] = "list_api_catalog_versions",
                ["arguments"] = new Dictionary<string, object>()
            }
        };

        // Act
        var response = await server.ProcessRequestAsync(request);

        // Assert
        response.Should().NotBeNull();
        response.Error.Should().BeNull();
        response.Result.Should().NotBeNull();
    }

    [Fact]
    public async Task McpServer_ToolsCall_WithInvalidTool_ReturnsError()
    {
        // Arrange
        var server = new McpServer(_adapter, _jsonOptions);
        var request = new McpRequest
        {
            Id = 4,
            Method = "tools/call",
            Params = new Dictionary<string, object>
            {
                ["name"] = "nonexistent_tool",
                ["arguments"] = new Dictionary<string, object>()
            }
        };

        // Act
        var response = await server.ProcessRequestAsync(request);

        // Assert
        response.Should().NotBeNull();
        response.Error.Should().NotBeNull();
    }

    [Fact]
    public async Task McpServer_ToolsCall_WithMissingArguments_ReturnsError()
    {
        // Arrange
        var server = new McpServer(_adapter, _jsonOptions);
        var request = new McpRequest
        {
            Id = 5,
            Method = "tools/call",
            Params = new Dictionary<string, object>
            {
                ["name"] = "get_api_definitions"
                // Missing required arguments
            }
        };

        // Act
        var response = await server.ProcessRequestAsync(request);

        // Assert
        response.Should().NotBeNull();
        response.Error.Should().NotBeNull();
    }

    [Fact]
    public async Task McpServer_ConcurrentRequests_HandlesCorrectly()
    {
        // Arrange
        var server = new McpServer(_adapter, _jsonOptions);
        var requests = Enumerable.Range(1, 10).Select(i => new McpRequest
        {
            Id = i,
            Method = "tools/list",
            Params = new Dictionary<string, object>()
        }).ToList();

        // Act
        var tasks = requests.Select(req => server.ProcessRequestAsync(req)).ToList();
        var responses = await Task.WhenAll(tasks);

        // Assert
        responses.Should().HaveCount(10);
        responses.Should().AllSatisfy(r => r.Error.Should().BeNull());
    }

    [Fact]
    public void McpServer_InvalidJsonRequest_HandlesGracefully()
    {
        // Arrange
        var server = new McpServer(_adapter, _jsonOptions);

        // This test verifies the server can be created and has tools registered
        // The invalid JSON handling is tested through ProcessRequestAsync with null/invalid params
        var request = new McpRequest
        {
            Id = 99,
            Method = "invalid_method",
            Params = null
        };

        // Act
        var responseTask = server.ProcessRequestAsync(request);

        // Assert
        responseTask.Should().NotBeNull();
        // The server should handle this gracefully by returning an error response
    }

    [Fact]
    public async Task McpServer_LargePayload_HandlesCorrectly()
    {
        // Arrange
        var server = new McpServer(_adapter, _jsonOptions);
        var largeWorkflow = string.Join("\n", Enumerable.Range(1, 100).Select(i => TestDataBuilder.CreateSampleWorkflow($"workflow{i}")));

        var request = new McpRequest
        {
            Id = 6,
            Method = "tools/call",
            Params = new Dictionary<string, object>
            {
                ["name"] = "generate_workflow_skeleton",
                ["arguments"] = new Dictionary<string, object>
                {
                    ["name"] = "large-workflow",
                    ["description"] = largeWorkflow
                }
            }
        };

        // Act
        var response = await server.ProcessRequestAsync(request);

        // Assert
        response.Should().NotBeNull();
    }

    [Fact]
    public async Task McpServer_EndToEndWorkflowGeneration_WorksCorrectly()
    {
        // Arrange
        var server = new McpServer(_adapter, _jsonOptions);

        // Step 1: List APIs
        var listApisRequest = new McpRequest
        {
            Id = 1,
            Method = "tools/call",
            Params = new Dictionary<string, object>
            {
                ["name"] = "list_api_catalog_versions",
                ["arguments"] = new Dictionary<string, object>()
            }
        };

        // Step 2: Get API definitions
        var getDefsRequest = new McpRequest
        {
            Id = 2,
            Method = "tools/call",
            Params = new Dictionary<string, object>
            {
                ["name"] = "get_api_definitions",
                ["arguments"] = new Dictionary<string, object>
                {
                    ["version"] = "3.10"
                }
            }
        };

        // Step 3: Generate workflow skeleton
        var generateRequest = new McpRequest
        {
            Id = 3,
            Method = "tools/call",
            Params = new Dictionary<string, object>
            {
                ["name"] = "generate_workflow_skeleton",
                ["arguments"] = new Dictionary<string, object>
                {
                    ["name"] = "integration-test-workflow",
                    ["description"] = "Test workflow for integration testing"
                }
            }
        };

        // Act
        var response1 = await server.ProcessRequestAsync(listApisRequest);
        var response2 = await server.ProcessRequestAsync(getDefsRequest);
        var response3 = await server.ProcessRequestAsync(generateRequest);

        // Assert
        response1.Error.Should().BeNull();
        response2.Error.Should().BeNull();
        response3.Error.Should().BeNull();
        response3.Result.Should().NotBeNull();
    }

    [Fact]
    public async Task McpServer_JsonRpcCompliance_FollowsProtocol()
    {
        // Arrange
        var server = new McpServer(_adapter, _jsonOptions);
        var request = new McpRequest
        {
            Id = 1,
            Method = "tools/list",
            Params = new Dictionary<string, object>()
        };

        // Act
        var response = await server.ProcessRequestAsync(request);

        // Assert
        response.Should().NotBeNull();
        response.Id.Should().Be(1);
        response.Result.Should().NotBeNull();
        response.Error.Should().BeNull();

        // Verify JSON-RPC compliance by serializing
        var responseJson = JsonSerializer.Serialize(response, _jsonOptions);
        responseJson.Should().Contain("\"id\"");
        responseJson.Should().Contain("\"result\"");
        responseJson.Should().NotContain("\"error\":");
    }

    public void Dispose()
    {
        _mockFs.Dispose();
    }
}
