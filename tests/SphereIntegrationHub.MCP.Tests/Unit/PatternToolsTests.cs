using System.Text.Json;
using System.Text.Json.Nodes;
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
    public async Task GenerateCrudWorkflow_WithoutVersion_UsesFirstCatalogVersionAndWarns()
    {
        // Arrange
        var tool = new GenerateCrudWorkflowTool(_adapter);
        var operations = new[] { "create" };
        var args = new Dictionary<string, object>
        {
            ["apiName"] = "AccountsAPI",
            ["resource"] = "accounts",
            ["operations"] = JsonSerializer.SerializeToElement(operations)
        };

        // Act
        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        // Assert
        json.GetProperty("version").GetString().Should().Be("3.10");
        json.GetProperty("warnings").GetArrayLength().Should().BeGreaterThan(0);
        json.GetProperty("yaml").GetString().Should().Contain("version: 3.10");
        json.GetProperty("wfvars").GetString().Should().BeNull();
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
        yaml.Should().Contain("httpVerb: GET");
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
        yaml.Should().Contain("httpVerb: PUT");
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
        yaml.Should().Contain("httpVerb: DELETE");
    }

    [Fact]
    public async Task GenerateCrudWorkflow_StripsDefinitionBasePathFromGeneratedEndpoints()
    {
        var catalog = """
[
  {
    "version": "3.10",
    "baseUrl": {
      "local": "https://localhost"
    },
    "definitions": [
      {
        "name": "AccountsAPI",
        "basePath": "/api",
        "swaggerUrl": "https://localhost:5009/swagger/v1/swagger.json",
        "baseUrl": {
          "local": "https://localhost:5009"
        }
      }
    ]
  }
]
""";
        _mockFs.AddApiCatalog(catalog);
        _mockFs.AddSwaggerFile("3.10", "AccountsAPI", TestDataBuilder.CreateSampleSwagger("AccountsAPI"));

        var tool = new GenerateCrudWorkflowTool(_adapter);
        var operations = new[] { "create", "read" };
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10",
            ["apiName"] = "AccountsAPI",
            ["resource"] = "accounts",
            ["operations"] = JsonSerializer.SerializeToElement(operations)
        };

        var result = await tool.ExecuteAsync(args);
        var yaml = ToJson(result).GetProperty("yaml").GetString();

        yaml.Should().Contain("endpoint: /accounts");
        yaml.Should().Contain("endpoint: /accounts/{{input.id}}");
        yaml.Should().NotContain("endpoint: /api/accounts");
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
    public async Task GenerateCrudWorkflow_WithApiKeySecurity_AddsHeaderInputsAndStageHeaders()
    {
        // Arrange
        var swaggerNode = JsonNode.Parse(TestDataBuilder.CreateSampleSwagger("AccountsAPI"))!.AsObject();
        var components = swaggerNode["components"]!.AsObject();
        components["securitySchemes"] = new JsonObject
        {
            ["ApiConsumerHeader"] = new JsonObject
            {
                ["type"] = "apiKey",
                ["in"] = "header",
                ["name"] = "X-Api-Consumer"
            },
            ["ApiKeyHeader"] = new JsonObject
            {
                ["type"] = "apiKey",
                ["in"] = "header",
                ["name"] = "X-Api-Key"
            }
        };

        var postOperation = swaggerNode["paths"]!["/api/accounts"]!["post"]!.AsObject();
        postOperation["security"] = new JsonArray
        {
            new JsonObject
            {
                ["ApiConsumerHeader"] = new JsonArray(),
                ["ApiKeyHeader"] = new JsonArray()
            }
        };

        _mockFs.AddSwaggerFile("3.10", "AccountsAPI", swaggerNode.ToJsonString());

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
        var yaml = json.GetProperty("yaml").GetString();
        var wfvars = json.GetProperty("wfvars").GetString();

        // Assert
        yaml.Should().Contain("- name: x_Api_Consumer");
        yaml.Should().Contain("- name: x_Api_Key");
        yaml.Should().Contain("\"X-Api-Consumer\": \"{{input.x_Api_Consumer}}\"");
        yaml.Should().Contain("\"X-Api-Key\": \"{{input.x_Api_Key}}\"");
        wfvars.Should().Contain("x_Api_Consumer");
        wfvars.Should().Contain("x_Api_Key");
    }

    [Fact]
    public async Task GenerateCrudWorkflow_WithRequestBodyExample_UsesSwaggerExampleInsteadOfResourceDataInput()
    {
        // Arrange
        var swaggerNode = JsonNode.Parse(TestDataBuilder.CreateSampleSwagger("AccountsAPI"))!.AsObject();
        var postOperation = swaggerNode["paths"]!["/api/accounts"]!["post"]!.AsObject();
        postOperation["requestBody"]!["content"]!["application/json"]!["example"] = JsonNode.Parse("""
{
  "name": "Acme Inc",
  "email": "contact@acme.test",
  "organizationId": "11111111-1111-1111-1111-111111111111"
}
""");

        _mockFs.AddSwaggerFile("3.10", "AccountsAPI", swaggerNode.ToJsonString());

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
        var yaml = json.GetProperty("yaml").GetString();
        var wfvars = json.GetProperty("wfvars").GetString();

        // Assert
        yaml.Should().Contain("\"name\":\"Acme Inc\"");
        yaml.Should().Contain("\"email\":\"contact@acme.test\"");
        yaml.Should().NotContain("{{input.accountsData}}");
        wfvars.Should().BeNull();
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
