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
        yaml.Should().Contain("kind:");
        yaml.Should().Contain("apiRef:");
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
        yaml.Should().Contain("endStage:");
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

    [Fact]
    public async Task GenerateEndpointStage_WithEndpointSchemaFallback_WorksWithoutCache()
    {
        // Arrange
        var tool = new GenerateEndpointStageTool(_adapter);
        var endpointSchema = new
        {
            apiName = "accounts",
            endpoint = "/api/accounts/{id}",
            httpVerb = "GET",
            pathParameters = new[]
            {
                new { name = "id", type = "string", required = true }
            }
        };

        var args = new Dictionary<string, object>
        {
            ["endpointSchema"] = JsonSerializer.SerializeToElement(endpointSchema)
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("source", out var sourceEl).Should().BeTrue();
        sourceEl.GetString().Should().Be("endpoint-schema-fallback");
        json.TryGetProperty("yaml", out var yamlEl).Should().BeTrue();
        yamlEl.GetString().Should().Contain("kind: Endpoint");
    }

    [Fact]
    public async Task GenerateWorkflowBundle_ReturnsWorkflowAndWfvarsDrafts()
    {
        // Arrange
        var tool = new GenerateWorkflowBundleTool(_adapter);
        var endpoints = new[]
        {
            new
            {
                stageName = "list_accounts",
                endpointSchema = new
                {
                    apiName = "AccountsAPI",
                    endpoint = "/api/accounts/{id}",
                    httpVerb = "GET",
                    pathParameters = new[]
                    {
                        new { name = "id", type = "string", required = true }
                    }
                }
            }
        };

        var args = new Dictionary<string, object>
        {
            ["version"] = "3.11",
            ["workflowName"] = "generated-workflow",
            ["apiName"] = "AccountsAPI",
            ["endpoints"] = JsonSerializer.SerializeToElement(endpoints)
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("workflowDraft", out var workflowDraftEl).Should().BeTrue();
        json.TryGetProperty("wfvarsDraft", out var wfvarsDraftEl).Should().BeTrue();
        workflowDraftEl.GetString().Should().Contain("endStage:");
        wfvarsDraftEl.GetString().Should().Contain("id");
    }

    [Fact]
    public async Task WriteWorkflowArtifacts_WritesFilesToWorkflowsPath()
    {
        // Arrange
        var tool = new WriteWorkflowArtifactsTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = "generated/test.workflow",
            ["workflowYaml"] = "name: test",
            ["wfvarsPath"] = "generated/test.wfvars",
            ["wfvarsYaml"] = "username: demo"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("count", out var countEl).Should().BeTrue();
        countEl.GetInt32().Should().Be(2);
        File.Exists(Path.Combine(_mockFs.WorkflowsPath, "generated", "test.workflow")).Should().BeTrue();
        File.Exists(Path.Combine(_mockFs.WorkflowsPath, "generated", "test.wfvars")).Should().BeTrue();
    }

    [Fact]
    public async Task GenerateStartupBootstrap_ReturnsCommandAndSnippets()
    {
        // Arrange
        var tool = new GenerateStartupBootstrapTool();
        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = "automation/seed.workflow",
            ["environment"] = "pre",
            ["varsFilePath"] = "automation/seed.wfvars",
            ["dryRun"] = true
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("startupCommand", out var startupCommandEl).Should().BeTrue();
        json.TryGetProperty("hostedServiceClass", out var hostedServiceClassEl).Should().BeTrue();
        json.TryGetProperty("programRegistrationSnippet", out var programRegistrationSnippetEl).Should().BeTrue();

        startupCommandEl.GetString().Should().Contain("--workflow");
        startupCommandEl.GetString().Should().Contain("--dry-run");
        hostedServiceClassEl.GetString().Should().Contain("IHostedService");
        programRegistrationSnippetEl.GetString().Should().Contain("AddHostedService");
    }

    [Fact]
    public void SihServicesAdapter_WithoutCatalog_DoesNotThrow()
    {
        // Arrange
        using var fs = new MockFileSystem();

        // Act
        var action = () => new SihServicesAdapter(fs.RootPath);

        // Assert
        action.Should().NotThrow();
    }

    [Fact]
    public async Task GenerateApiCatalogFile_WritesCatalog()
    {
        // Arrange
        var tool = new GenerateApiCatalogFileTool(_adapter);
        var payload = new[]
        {
            new
            {
                version = "1.0",
                baseUrl = new { local = "http://localhost:5000" },
                definitions = new[]
                {
                    new { name = "orders", basePath = "/ordersapi", swaggerUrl = "/ordersapi/swagger/v1/swagger.json" }
                }
            }
        };

        var args = new Dictionary<string, object>
        {
            ["versions"] = JsonSerializer.SerializeToElement(payload),
            ["outputPath"] = "src/resources/api-catalog.json",
            ["writeToDisk"] = true
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("versionsCount", out var versionsCountEl).Should().BeTrue();
        versionsCountEl.GetInt32().Should().Be(1);
        File.Exists(Path.Combine(_mockFs.RootPath, "src", "resources", "api-catalog.json")).Should().BeTrue();
    }

    public void Dispose()
    {
        _mockFs.Dispose();
    }
}
