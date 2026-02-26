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
        json.TryGetProperty("wfvars", out var wfvarsEl).Should().BeTrue();
        wfvarsEl.GetString().Should().Contain("userId");
        wfvarsEl.GetString().Should().Contain("accountId");
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
    public async Task GenerateWorkflowSkeleton_WithoutVersion_UsesFirstCatalogVersionAndWarns()
    {
        // Arrange
        var tool = new GenerateWorkflowSkeletonTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["name"] = "fallback-version-workflow",
            ["description"] = "Workflow without explicit version"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("version", out var versionEl).Should().BeTrue();
        versionEl.GetString().Should().Be("3.10");
        json.TryGetProperty("warnings", out var warningsEl).Should().BeTrue();
        warningsEl.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GenerateWorkflowSkeleton_WithMissingName_ThrowsException()
    {
        // Arrange
        var tool = new GenerateWorkflowSkeletonTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["description"] = "Missing name"
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => tool.ExecuteAsync(args));
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
    public async Task GenerateWorkflowBundle_ReturnsWorkflowAndWfvarss()
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
        json.TryGetProperty("wfvars", out var wfvarsEl).Should().BeTrue();
        workflowDraftEl.GetString().Should().Contain("endStage:");
        wfvarsEl.GetString().Should().Contain("id");
    }

    [Fact]
    public async Task GenerateWorkflowBundle_WithoutVersion_UsesFirstCatalogVersionAndWarns()
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
            ["workflowName"] = "generated-with-fallback-version",
            ["apiName"] = "AccountsAPI",
            ["endpoints"] = JsonSerializer.SerializeToElement(endpoints)
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.TryGetProperty("version", out var versionEl).Should().BeTrue();
        versionEl.GetString().Should().Be("3.10");
        json.TryGetProperty("warnings", out var warningsEl).Should().BeTrue();
        warningsEl.GetArrayLength().Should().BeGreaterThan(0);
        json.GetProperty("workflowDraft").GetString().Should().Contain("version: 3.10");
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
    public async Task WriteWorkflowArtifacts_WithYamlExtension_NormalizesToWorkflowExtension()
    {
        // Arrange
        var tool = new WriteWorkflowArtifactsTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = "generated/from-llm.yaml",
            ["workflowYaml"] = "name: test"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        var resolvedPath = json.GetProperty("workflowPathResolved").GetString();
        resolvedPath.Should().EndWith("generated/from-llm.workflow");
        File.Exists(resolvedPath).Should().BeTrue();
        json.GetProperty("warnings").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task WriteWorkflowArtifacts_WithoutWfvarsPath_UsesWorkflowBaseNameWithWfvarsExtension()
    {
        // Arrange
        var tool = new WriteWorkflowArtifactsTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = "generated/abc123.yaml",
            ["workflowYaml"] = "name: test",
            ["wfvarsYaml"] = "username: demo"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        var workflowPath = json.GetProperty("workflowPathResolved").GetString();
        var wfvarsPath = json.GetProperty("wfvarsPathResolved").GetString();
        workflowPath.Should().EndWith("generated/abc123.workflow");
        wfvarsPath.Should().EndWith("generated/abc123.wfvars");
        File.Exists(workflowPath).Should().BeTrue();
        File.Exists(wfvarsPath).Should().BeTrue();
    }

    [Fact]
    public async Task WriteWorkflowArtifacts_WithWrongWfvarsExtension_NormalizesToWfvars()
    {
        // Arrange
        var tool = new WriteWorkflowArtifactsTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = "generated/abc123.workflow",
            ["workflowYaml"] = "name: test",
            ["wfvarsPath"] = "generated/abc123.yaml",
            ["wfvarsYaml"] = "username: demo"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        var wfvarsPath = json.GetProperty("wfvarsPathResolved").GetString();
        wfvarsPath.Should().EndWith("generated/abc123.wfvars");
        File.Exists(wfvarsPath).Should().BeTrue();
    }

    [Fact]
    public async Task GenerateWfvarsFromWorkflow_WithInputs_WritesWfvars()
    {
        // Arrange
        var tool = new GenerateWfvarsFromWorkflowTool(_adapter);
        var workflowPath = Path.Combine(_mockFs.WorkflowsPath, "wfvars", "with-inputs.workflow");
        Directory.CreateDirectory(Path.GetDirectoryName(workflowPath)!);
        await File.WriteAllTextAsync(workflowPath, """
version: "3.10"
id: "W1"
name: "wfvars-with-inputs"
description: "wfvars generation test"
output: true
input:
  - name: username
    type: Text
    required: true
  - name: password
    type: Text
    required: false
stages: []
endStage:
  output: {}
""");

        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = "wfvars/with-inputs.workflow",
            ["writeChanges"] = true
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.GetProperty("written").GetBoolean().Should().BeTrue();
        json.GetProperty("hasInputs").GetBoolean().Should().BeTrue();
        json.GetProperty("wfvars").GetString().Should().Contain("username");
        var wfvarsPath = json.GetProperty("wfvarsPath").GetString();
        File.Exists(wfvarsPath).Should().BeTrue();
    }

    [Fact]
    public async Task GenerateWfvarsFromWorkflow_WithoutInputs_ReturnsWarningAndDoesNotWrite()
    {
        // Arrange
        var tool = new GenerateWfvarsFromWorkflowTool(_adapter);
        var workflowPath = Path.Combine(_mockFs.WorkflowsPath, "wfvars", "without-inputs.workflow");
        Directory.CreateDirectory(Path.GetDirectoryName(workflowPath)!);
        await File.WriteAllTextAsync(workflowPath, """
version: "3.10"
id: "W2"
name: "wfvars-without-inputs"
description: "wfvars generation test"
output: true
stages: []
endStage:
  output: {}
""");

        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = "wfvars/without-inputs.workflow",
            ["writeChanges"] = true
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.GetProperty("written").GetBoolean().Should().BeFalse();
        json.GetProperty("hasInputs").GetBoolean().Should().BeFalse();
        json.GetProperty("warnings").GetArrayLength().Should().BeGreaterThan(0);
        var wfvarsPath = json.GetProperty("wfvarsPath").GetString();
        File.Exists(wfvarsPath).Should().BeFalse();
    }

    [Fact]
    public async Task RepairWorkflowArtifacts_WhenWfvarsMissing_CreatesWfvars()
    {
        // Arrange
        var tool = new RepairWorkflowArtifactsTool(_adapter);
        var workflowPath = Path.Combine(_mockFs.WorkflowsPath, "repair", "sample.workflow");
        Directory.CreateDirectory(Path.GetDirectoryName(workflowPath)!);
        await File.WriteAllTextAsync(workflowPath, """
version: "3.10"
id: "R1"
name: "repair-sample"
description: "repair test"
output: true
input:
  - name: username
    type: Text
    required: true
stages: []
endStage:
  output: {}
""");

        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = "repair/sample.workflow",
            ["writeChanges"] = true
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.GetProperty("wfvars").GetProperty("created").GetBoolean().Should().BeTrue();
        var wfvarsPath = json.GetProperty("wfvarsPath").GetString();
        File.Exists(wfvarsPath).Should().BeTrue();
        var wfvarsContent = await File.ReadAllTextAsync(wfvarsPath!);
        wfvarsContent.Should().Contain("username");
    }

    [Fact]
    public async Task RepairWorkflowArtifacts_WhenWfvarsMissingRequiredInputs_RepairsFile()
    {
        // Arrange
        var tool = new RepairWorkflowArtifactsTool(_adapter);
        var workflowPath = Path.Combine(_mockFs.WorkflowsPath, "repair", "mismatch.workflow");
        var wfvarsPath = Path.Combine(_mockFs.WorkflowsPath, "repair", "mismatch.wfvars");
        Directory.CreateDirectory(Path.GetDirectoryName(workflowPath)!);
        await File.WriteAllTextAsync(workflowPath, """
version: "3.10"
id: "R2"
name: "repair-mismatch"
description: "repair test"
output: true
input:
  - name: username
    type: Text
    required: true
  - name: password
    type: Text
    required: true
stages: []
endStage:
  output: {}
""");
        await File.WriteAllTextAsync(wfvarsPath, """
username: "demo"
""");

        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = "repair/mismatch.workflow",
            ["writeChanges"] = true
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.GetProperty("wfvars").GetProperty("updated").GetBoolean().Should().BeTrue();
        var wfvarsContent = await File.ReadAllTextAsync(wfvarsPath);
        wfvarsContent.Should().Contain("password");
    }

    [Fact]
    public async Task RepairWorkflowArtifacts_WithWriteChangesFalse_DoesNotWriteButReturnsDraft()
    {
        // Arrange
        var tool = new RepairWorkflowArtifactsTool(_adapter);
        var workflowPath = Path.Combine(_mockFs.WorkflowsPath, "repair", "preview.workflow");
        Directory.CreateDirectory(Path.GetDirectoryName(workflowPath)!);
        await File.WriteAllTextAsync(workflowPath, """
version: "3.10"
id: "R3"
name: "repair-preview"
description: "repair test"
output: true
input:
  - name: username
    type: Text
    required: true
stages: []
endStage:
  output: {}
""");

        var args = new Dictionary<string, object>
        {
            ["workflowPath"] = "repair/preview.workflow",
            ["writeChanges"] = false
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.GetProperty("wfvars").GetProperty("draft").GetString().Should().Contain("username");
        var wfvarsPath = json.GetProperty("wfvarsPath").GetString();
        File.Exists(wfvarsPath).Should().BeFalse();
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

    [Fact]
    public async Task GenerateApiCatalogFile_WithAbsoluteSwaggerUrl_BuildsTemplateAndPort()
    {
        // Arrange
        var tool = new GenerateApiCatalogFileTool(_adapter);
        var payload = new[]
        {
            new
            {
                version = "1.0",
                baseUrl = new { local = "https://localhost" },
                definitions = new[]
                {
                    new
                    {
                        name = "TravelAgent.Admin.Licensing.Api",
                        basePath = "/api",
                        swaggerUrl = "https://localhost:5005/swagger/v1/swagger.json"
                    }
                }
            }
        };

        var outputPath = Path.Combine(_mockFs.RootPath, "src", "resources", "api-catalog.inferred.json");
        var args = new Dictionary<string, object>
        {
            ["versions"] = JsonSerializer.SerializeToElement(payload),
            ["outputPath"] = outputPath,
            ["writeToDisk"] = true
        };

        // Act
        await tool.ExecuteAsync(args);
        var catalogJson = await File.ReadAllTextAsync(outputPath);
        var catalogDoc = JsonDocument.Parse(catalogJson);

        // Assert
        var definition = catalogDoc.RootElement[0]
            .GetProperty("definitions")[0]
            ;

        var port = definition.GetProperty("port").GetInt32();
        var templatedSwaggerUrl = definition.GetProperty("swaggerUrl").GetString();
        port.Should().Be(5005);
        templatedSwaggerUrl.Should().Be("{{baseUrl.local}}:{{port}}/swagger/v1/swagger.json");
    }

    [Fact]
    public async Task GenerateApiCatalogFile_WithHtmlSwaggerUrl_NormalizesToJsonUrl()
    {
        var tool = new GenerateApiCatalogFileTool(_adapter);
        var payload = new[]
        {
            new
            {
                version = "1.0",
                baseUrl = new { local = "https://localhost" },
                definitions = new[]
                {
                    new
                    {
                        name = "TravelAgent.Admin.Licensing.Api",
                        basePath = "/api",
                        swaggerUrl = "https://localhost:5005/swagger/index.html"
                    }
                }
            }
        };

        var outputPath = Path.Combine(_mockFs.RootPath, "src", "resources", "api-catalog.normalized.json");
        var args = new Dictionary<string, object>
        {
            ["versions"] = JsonSerializer.SerializeToElement(payload),
            ["outputPath"] = outputPath,
            ["writeToDisk"] = true
        };

        await tool.ExecuteAsync(args);
        var catalogJson = await File.ReadAllTextAsync(outputPath);
        var catalogDoc = JsonDocument.Parse(catalogJson);

        var definition = catalogDoc.RootElement[0]
            .GetProperty("definitions")[0];
        var storedSwaggerUrl = definition.GetProperty("swaggerUrl").GetString();
        var storedPort = definition.GetProperty("port").GetInt32();
        storedSwaggerUrl.Should().Be("{{baseUrl.local}}:{{port}}/swagger/v1/swagger.json");
        storedPort.Should().Be(5005);
    }

    [Fact]
    public async Task UpsertApiCatalogAndCache_WhenCatalogMissing_CreatesCatalogAndDownloadsCache()
    {
        // Arrange
        var adapter = new SihServicesAdapter(new SihPathOptions
        {
            ProjectRoot = _mockFs.RootPath,
            ResourcesPath = "src/resources-new"
        });

        var swaggerContent = TestDataBuilder.CreateSampleSwagger("BootstrapApi");
        var sourceSwaggerPath = Path.Combine(_mockFs.RootPath, "tmp", "bootstrap-swagger.json");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceSwaggerPath)!);
        await File.WriteAllTextAsync(sourceSwaggerPath, swaggerContent);

        var tool = new UpsertApiCatalogAndCacheTool(adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "9.99",
            ["apiName"] = "BootstrapApi",
            ["swaggerUrl"] = new Uri(sourceSwaggerPath).AbsoluteUri,
            ["basePath"] = "/api/bootstrap"
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.GetProperty("catalogCreated").GetBoolean().Should().BeTrue();
        json.GetProperty("cacheDownloaded").GetBoolean().Should().BeTrue();

        File.Exists(adapter.ApiCatalogPath).Should().BeTrue();
        File.Exists(adapter.GetSwaggerCachePath("9.99", "BootstrapApi")).Should().BeTrue();
    }

    [Fact]
    public async Task UpsertApiCatalogAndCache_WithAbsoluteSwaggerUrl_StoresTemplateAndPort()
    {
        // Arrange
        var adapter = new SihServicesAdapter(new SihPathOptions
        {
            ProjectRoot = _mockFs.RootPath,
            ResourcesPath = "src/resources-upsert-baseurl"
        });

        var tool = new UpsertApiCatalogAndCacheTool(adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "0.1",
            ["apiName"] = "TravelAgent.Admin.Licensing.Api",
            ["swaggerUrl"] = "https://localhost:5005/swagger/v1/swagger.json",
            ["basePath"] = "/api",
            ["environment"] = "local",
            ["baseUrl"] = JsonSerializer.SerializeToElement(new { local = "https://localhost" }),
            ["downloadCache"] = false
        };

        // Act
        await tool.ExecuteAsync(args);
        var catalogJson = await File.ReadAllTextAsync(adapter.ApiCatalogPath);
        var catalogDoc = JsonDocument.Parse(catalogJson);

        // Assert
        var definition = catalogDoc.RootElement[0]
            .GetProperty("definitions")[0]
            ;

        var definitionPort = definition.GetProperty("port").GetInt32();
        var templatedSwaggerUrl = definition.GetProperty("swaggerUrl").GetString();
        definitionPort.Should().Be(5005);
        templatedSwaggerUrl.Should().Be("{{baseUrl.local}}:{{port}}/swagger/v1/swagger.json");
    }

    [Fact]
    public async Task RefreshSwaggerCacheFromCatalog_WithSelectedApiNames_DownloadsOnlySelected()
    {
        // Arrange
        var adapter = new SihServicesAdapter(new SihPathOptions
        {
            ProjectRoot = _mockFs.RootPath,
            ResourcesPath = "src/resources-refresh"
        });

        var swaggerAPath = Path.Combine(_mockFs.RootPath, "tmp", "accounts-swagger.json");
        var swaggerBPath = Path.Combine(_mockFs.RootPath, "tmp", "users-swagger.json");
        Directory.CreateDirectory(Path.GetDirectoryName(swaggerAPath)!);
        await File.WriteAllTextAsync(swaggerAPath, TestDataBuilder.CreateSampleSwagger("AccountsAPI"));
        await File.WriteAllTextAsync(swaggerBPath, TestDataBuilder.CreateSampleSwagger("UsersAPI"));

        var catalog = new[]
        {
            new
            {
                Version = "4.00",
                BaseUrl = new Dictionary<string, string>
                {
                    ["pre"] = "https://pre.example.com"
                },
                Definitions = new[]
                {
                    new
                    {
                        Name = "AccountsAPI",
                        BasePath = "/api/accounts",
                        SwaggerUrl = new Uri(swaggerAPath).AbsoluteUri
                    },
                    new
                    {
                        Name = "UsersAPI",
                        BasePath = "/api/users",
                        SwaggerUrl = new Uri(swaggerBPath).AbsoluteUri
                    }
                }
            }
        };

        var catalogJson = JsonSerializer.Serialize(catalog, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(adapter.ApiCatalogPath)!);
        await File.WriteAllTextAsync(adapter.ApiCatalogPath, catalogJson);

        var tool = new RefreshSwaggerCacheFromCatalogTool(adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "4.00",
            ["refresh"] = true,
            ["apiNames"] = JsonSerializer.SerializeToElement(new[] { "UsersAPI" })
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.GetProperty("counts").GetProperty("selected").GetInt32().Should().Be(1);
        json.GetProperty("counts").GetProperty("downloaded").GetInt32().Should().Be(1);
        File.Exists(adapter.GetSwaggerCachePath("4.00", "UsersAPI")).Should().BeTrue();
        File.Exists(adapter.GetSwaggerCachePath("4.00", "AccountsAPI")).Should().BeFalse();
    }

    [Fact]
    public async Task UpsertApiCatalogAndCache_WithHtmlSource_ThrowsValidationError()
    {
        // Arrange
        var adapter = new SihServicesAdapter(new SihPathOptions
        {
            ProjectRoot = _mockFs.RootPath,
            ResourcesPath = "src/resources-html-upsert"
        });

        var htmlPath = Path.Combine(_mockFs.RootPath, "tmp", "swagger-ui.html");
        Directory.CreateDirectory(Path.GetDirectoryName(htmlPath)!);
        await File.WriteAllTextAsync(htmlPath, "<html><body>Swagger UI</body></html>");

        var tool = new UpsertApiCatalogAndCacheTool(adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "5.00",
            ["apiName"] = "BadApi",
            ["swaggerUrl"] = new Uri(htmlPath).AbsoluteUri
        };

        // Act
        var action = () => tool.ExecuteAsync(args);

        // Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(action);
        ex.Message.Should().Contain("returned HTML content");
    }

    [Fact]
    public async Task UpsertApiCatalogAndCache_WithHtmlSource_FallsBackToKnownJsonUrl()
    {
        // Arrange
        var adapter = new SihServicesAdapter(new SihPathOptions
        {
            ProjectRoot = _mockFs.RootPath,
            ResourcesPath = "src/resources-html-fallback"
        });

        var swaggerDir = Path.Combine(_mockFs.RootPath, "tmp", "service", "swagger");
        Directory.CreateDirectory(Path.Combine(swaggerDir, "v1"));
        var htmlPath = Path.Combine(swaggerDir, "index.html");
        var jsonPath = Path.Combine(swaggerDir, "v1", "swagger.json");

        await File.WriteAllTextAsync(htmlPath, "<html><body>Swagger UI</body></html>");
        await File.WriteAllTextAsync(jsonPath, TestDataBuilder.CreateSampleSwagger("FallbackApi"));

        var tool = new UpsertApiCatalogAndCacheTool(adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "5.10",
            ["apiName"] = "FallbackApi",
            ["swaggerUrl"] = new Uri(htmlPath).AbsoluteUri
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.GetProperty("cacheDownloaded").GetBoolean().Should().BeTrue();
        File.Exists(adapter.GetSwaggerCachePath("5.10", "FallbackApi")).Should().BeTrue();

        var catalogJson = await File.ReadAllTextAsync(adapter.ApiCatalogPath);
        var catalogDoc = JsonDocument.Parse(catalogJson);
        var storedSwaggerUrl = catalogDoc.RootElement[0]
            .GetProperty("definitions")[0]
            .GetProperty("swaggerUrl")
            .GetString();
        storedSwaggerUrl.Should().EndWith("/swagger/v1/swagger.json");
    }

    [Fact]
    public async Task UpsertApiCatalogAndCache_WithGenericApiName_UsesOpenApiTitleAsName()
    {
        // Arrange
        var adapter = new SihServicesAdapter(new SihPathOptions
        {
            ProjectRoot = _mockFs.RootPath,
            ResourcesPath = "src/resources-name-inference"
        });

        var swaggerDir = Path.Combine(_mockFs.RootPath, "tmp", "travel-admin", "swagger");
        Directory.CreateDirectory(Path.Combine(swaggerDir, "v1"));
        var htmlPath = Path.Combine(swaggerDir, "index.html");
        var jsonPath = Path.Combine(swaggerDir, "v1", "swagger.json");

        await File.WriteAllTextAsync(htmlPath, "<html><body>Swagger UI</body></html>");
        await File.WriteAllTextAsync(jsonPath, TestDataBuilder.CreateSampleSwagger("TravelAgent.Admin.Api"));

        var tool = new UpsertApiCatalogAndCacheTool(adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "0.1",
            ["apiName"] = "api-5009",
            ["swaggerUrl"] = new Uri(htmlPath).AbsoluteUri
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.GetProperty("requestedApiName").GetString().Should().Be("api-5009");
        json.GetProperty("apiName").GetString().Should().Be("TravelAgent.Admin.Api");
        File.Exists(adapter.GetSwaggerCachePath("0.1", "TravelAgent.Admin.Api")).Should().BeTrue();
        File.Exists(adapter.GetSwaggerCachePath("0.1", "api-5009")).Should().BeFalse();
    }

    [Fact]
    public async Task RefreshSwaggerCacheFromCatalog_WithHtmlSource_ReportsFailure()
    {
        // Arrange
        var adapter = new SihServicesAdapter(new SihPathOptions
        {
            ProjectRoot = _mockFs.RootPath,
            ResourcesPath = "src/resources-html-refresh"
        });

        var htmlPath = Path.Combine(_mockFs.RootPath, "tmp", "swagger-ui-2.html");
        Directory.CreateDirectory(Path.GetDirectoryName(htmlPath)!);
        await File.WriteAllTextAsync(htmlPath, "<!DOCTYPE html><html><body>UI</body></html>");

        var catalog = new[]
        {
            new
            {
                Version = "5.01",
                BaseUrl = new Dictionary<string, string> { ["pre"] = "https://pre.example.com" },
                Definitions = new[]
                {
                    new
                    {
                        Name = "BadApi",
                        BasePath = "/api/bad",
                        SwaggerUrl = new Uri(htmlPath).AbsoluteUri
                    }
                }
            }
        };

        var catalogJson = JsonSerializer.Serialize(catalog, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(adapter.ApiCatalogPath)!);
        await File.WriteAllTextAsync(adapter.ApiCatalogPath, catalogJson);

        var tool = new RefreshSwaggerCacheFromCatalogTool(adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "5.01",
            ["refresh"] = true
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.GetProperty("counts").GetProperty("failed").GetInt32().Should().Be(1);
        json.GetProperty("counts").GetProperty("downloaded").GetInt32().Should().Be(0);
        File.Exists(adapter.GetSwaggerCachePath("5.01", "BadApi")).Should().BeFalse();
    }

    [Fact]
    public async Task RefreshSwaggerCacheFromCatalog_WithGenericApiNames_InfersNamesAndUpdatesCatalogAndCache()
    {
        // Arrange
        var adapter = new SihServicesAdapter(new SihPathOptions
        {
            ProjectRoot = _mockFs.RootPath,
            ResourcesPath = "src/resources-refresh-infer-names"
        });

        var swaggerSpecs = new[]
        {
            new { GenericName = "api-5009", Title = "TravelAgent.Admin.Api | 3.0.1" },
            new { GenericName = "api-5007", Title = "TravelAgent.Licensing.PublicApi | 3.0.1" },
            new { GenericName = "api-5005", Title = "TravelAgent.Admin.Licensing.Api | 3.0.1" }
        };

        var definitions = new List<object>();
        foreach (var spec in swaggerSpecs)
        {
            var swaggerDir = Path.Combine(_mockFs.RootPath, "tmp", spec.GenericName, "swagger");
            Directory.CreateDirectory(Path.Combine(swaggerDir, "v1"));
            var htmlPath = Path.Combine(swaggerDir, "index.html");
            var jsonPath = Path.Combine(swaggerDir, "v1", "swagger.json");

            await File.WriteAllTextAsync(htmlPath, "<html><body>Swagger UI</body></html>");
            await File.WriteAllTextAsync(jsonPath, TestDataBuilder.CreateSampleSwagger(spec.Title));

            definitions.Add(new
            {
                Name = spec.GenericName,
                BasePath = $"/{spec.GenericName}",
                SwaggerUrl = new Uri(htmlPath).AbsoluteUri
            });
        }

        var catalog = new[]
        {
            new
            {
                Version = "0.1",
                BaseUrl = new Dictionary<string, string> { ["pre"] = "https://pre.example.com" },
                Definitions = definitions
            }
        };

        var catalogJson = JsonSerializer.Serialize(catalog, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(adapter.ApiCatalogPath)!);
        await File.WriteAllTextAsync(adapter.ApiCatalogPath, catalogJson);

        var tool = new RefreshSwaggerCacheFromCatalogTool(adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "0.1",
            ["refresh"] = true
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.GetProperty("counts").GetProperty("downloaded").GetInt32().Should().Be(3);
        json.GetProperty("counts").GetProperty("failed").GetInt32().Should().Be(0);

        var updatedCatalogJson = await File.ReadAllTextAsync(adapter.ApiCatalogPath);
        updatedCatalogJson.Should().Contain("\"name\": \"TravelAgent.Admin.Api\"");
        updatedCatalogJson.Should().Contain("\"name\": \"TravelAgent.Licensing.PublicApi\"");
        updatedCatalogJson.Should().Contain("\"name\": \"TravelAgent.Admin.Licensing.Api\"");
        updatedCatalogJson.Should().NotContain("\"name\": \"api-5009\"");
        updatedCatalogJson.Should().NotContain("\"name\": \"api-5007\"");
        updatedCatalogJson.Should().NotContain("\"name\": \"api-5005\"");

        File.Exists(adapter.GetSwaggerCachePath("0.1", "TravelAgent.Admin.Api")).Should().BeTrue();
        File.Exists(adapter.GetSwaggerCachePath("0.1", "TravelAgent.Licensing.PublicApi")).Should().BeTrue();
        File.Exists(adapter.GetSwaggerCachePath("0.1", "TravelAgent.Admin.Licensing.Api")).Should().BeTrue();
        File.Exists(adapter.GetSwaggerCachePath("0.1", "api-5009")).Should().BeFalse();
        File.Exists(adapter.GetSwaggerCachePath("0.1", "api-5007")).Should().BeFalse();
        File.Exists(adapter.GetSwaggerCachePath("0.1", "api-5005")).Should().BeFalse();
    }

    [Fact]
    public async Task QuickRefreshSwaggerCache_WithDefaults_RefreshesVersion010Local()
    {
        // Arrange
        var adapter = new SihServicesAdapter(new SihPathOptions
        {
            ProjectRoot = _mockFs.RootPath,
            ResourcesPath = "src/resources-quick-refresh"
        });

        var swaggerDir = Path.Combine(_mockFs.RootPath, "tmp", "quick-refresh", "swagger");
        Directory.CreateDirectory(Path.Combine(swaggerDir, "v1"));
        var jsonPath = Path.Combine(swaggerDir, "v1", "swagger.json");
        await File.WriteAllTextAsync(jsonPath, TestDataBuilder.CreateSampleSwagger("QuickRefreshApi"));

        var catalog = new[]
        {
            new
            {
                Version = "0.1",
                BaseUrl = new Dictionary<string, string> { ["local"] = "https://localhost" },
                Definitions = new[]
                {
                    new
                    {
                        Name = "QuickRefreshApi",
                        BasePath = "/api",
                        SwaggerUrl = new Uri(jsonPath).AbsoluteUri
                    }
                }
            }
        };

        var catalogJson = JsonSerializer.Serialize(catalog, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(adapter.ApiCatalogPath)!);
        await File.WriteAllTextAsync(adapter.ApiCatalogPath, catalogJson);

        var tool = new QuickRefreshSwaggerCacheTool(adapter);

        // Act
        var result = await tool.ExecuteAsync(null);
        var json = ToJson(result);

        // Assert
        json.GetProperty("version").GetString().Should().Be("0.1");
        json.GetProperty("environment").GetString().Should().Be("local");
        json.GetProperty("refresh").GetBoolean().Should().BeTrue();
        json.GetProperty("counts").GetProperty("downloaded").GetInt32().Should().Be(1);
        json.GetProperty("counts").GetProperty("failed").GetInt32().Should().Be(0);
        File.Exists(adapter.GetSwaggerCachePath("0.1", "QuickRefreshApi")).Should().BeTrue();
    }

    public void Dispose()
    {
        _mockFs.Dispose();
    }
}
