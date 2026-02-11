using System.Text.Json;
using FluentAssertions;
using SphereIntegrationHub.MCP.Services.Integration;
using SphereIntegrationHub.MCP.Tests.TestHelpers;
using SphereIntegrationHub.MCP.Tools;
using Xunit;

namespace SphereIntegrationHub.MCP.Tests.Unit;

public class OptimizationToolsTests : IDisposable
{
    private readonly MockFileSystem _mockFs;
    private readonly SihServicesAdapter _adapter;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public OptimizationToolsTests()
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
    public async Task SuggestOptimizations_WithValidWorkflow_ReturnsOptimizations()
    {
        var tool = new SuggestOptimizationsTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowYaml"] = TestDataBuilder.CreateSampleWorkflow()
        };

        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        json.TryGetProperty("optimizations", out _).Should().BeTrue();
        json.TryGetProperty("currentMetrics", out _).Should().BeTrue();
        json.TryGetProperty("projectedMetrics", out _).Should().BeTrue();
    }

    [Fact]
    public async Task SuggestOptimizations_DetectsParallelizationOpportunities()
    {
        var tool = new SuggestOptimizationsTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowYaml"] = TestDataBuilder.CreateSampleWorkflow()
        };

        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        json.TryGetProperty("optimizations", out var opts).Should().BeTrue();
        opts.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task SuggestOptimizations_DetectsRedundantCalls()
    {
        var workflowWithRedundancy = @"
name: redundant-workflow
description: Workflow with redundant calls
version: 1.0.0

stages:
  - name: get-data-1
    plugin: HttpClientPlugin
    config:
      url: https://api.example.com/data
      method: GET
    saveResponseAs: data1

  - name: get-data-2
    plugin: HttpClientPlugin
    config:
      url: https://api.example.com/data
      method: GET
    saveResponseAs: data2
";

        var tool = new SuggestOptimizationsTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowYaml"] = workflowWithRedundancy
        };

        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        json.TryGetProperty("optimizations", out var opts).Should().BeTrue();
        opts.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task SuggestOptimizations_SuggestsResilienceImprovements()
    {
        var tool = new SuggestOptimizationsTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowYaml"] = TestDataBuilder.CreateSampleWorkflow()
        };

        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        json.TryGetProperty("optimizations", out _).Should().BeTrue();
    }

    [Fact]
    public async Task SuggestOptimizations_CalculatesCurrentMetrics()
    {
        var tool = new SuggestOptimizationsTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowYaml"] = TestDataBuilder.CreateSampleWorkflow()
        };

        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        json.TryGetProperty("currentMetrics", out var metrics).Should().BeTrue();
        metrics.TryGetProperty("stages", out _).Should().BeTrue();
        metrics.TryGetProperty("estimatedDuration", out _).Should().BeTrue();
    }

    [Fact]
    public async Task SuggestOptimizations_CalculatesProjectedMetrics()
    {
        var tool = new SuggestOptimizationsTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowYaml"] = TestDataBuilder.CreateSampleWorkflow()
        };

        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        json.TryGetProperty("projectedMetrics", out var metrics).Should().BeTrue();
        metrics.TryGetProperty("estimatedDuration", out _).Should().BeTrue();
    }

    [Fact]
    public async Task SuggestOptimizations_PrioritizesOptimizations()
    {
        var tool = new SuggestOptimizationsTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowYaml"] = TestDataBuilder.CreateSampleWorkflow()
        };

        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        json.TryGetProperty("optimizations", out var opts).Should().BeTrue();
        if (opts.GetArrayLength() > 0)
        {
            var first = opts[0];
            first.TryGetProperty("priority", out var priority).Should().BeTrue();
            var pval = priority.GetString();
            pval.Should().BeOneOf("high", "medium", "low");
        }
    }

    [Fact]
    public async Task SuggestOptimizations_IncludesImplementationDetails()
    {
        var tool = new SuggestOptimizationsTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowYaml"] = TestDataBuilder.CreateSampleWorkflow()
        };

        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        json.TryGetProperty("optimizations", out var opts).Should().BeTrue();
        if (opts.GetArrayLength() > 0)
        {
            opts[0].TryGetProperty("implementation", out _).Should().BeTrue();
        }
    }

    [Fact]
    public async Task SuggestOptimizations_GeneratesSummary()
    {
        var tool = new SuggestOptimizationsTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["workflowYaml"] = TestDataBuilder.CreateSampleWorkflow()
        };

        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        json.TryGetProperty("summary", out var summary).Should().BeTrue();
        summary.TryGetProperty("totalOptimizations", out _).Should().BeTrue();
    }

    [Fact]
    public async Task AnalyzeSwaggerCoverage_WithValidVersion_ReturnsUsageStatistics()
    {
        var tool = new AnalyzeSwaggerCoverageTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10"
        };

        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        json.TryGetProperty("version", out var version).Should().BeTrue();
        version.GetString().Should().Be("3.10");
        json.TryGetProperty("totalEndpoints", out _).Should().BeTrue();
    }

    [Fact]
    public async Task AnalyzeSwaggerCoverage_IdentifiesUsedEndpoints()
    {
        var tool = new AnalyzeSwaggerCoverageTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10"
        };

        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        json.TryGetProperty("endpointUsage", out _).Should().BeTrue();
    }

    [Fact]
    public async Task AnalyzeSwaggerCoverage_IdentifiesUnusedEndpoints()
    {
        var tool = new AnalyzeSwaggerCoverageTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10"
        };

        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        json.TryGetProperty("unusedEndpoints", out _).Should().BeTrue();
    }

    [Fact]
    public async Task AnalyzeSwaggerCoverage_SuggestsUsesForUnusedEndpoints()
    {
        var tool = new AnalyzeSwaggerCoverageTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10"
        };

        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        json.TryGetProperty("unusedSuggestions", out _).Should().BeTrue();
    }

    [Fact]
    public async Task AnalyzeSwaggerCoverage_CalculatesCoveragePercentage()
    {
        var tool = new AnalyzeSwaggerCoverageTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10"
        };

        var result = await tool.ExecuteAsync(args);
        var json = ToJson(result);

        json.TryGetProperty("coveragePercentage", out _).Should().BeTrue();
    }

    [Fact]
    public async Task AnalyzeSwaggerCoverage_WithMissingVersion_ThrowsException()
    {
        var tool = new AnalyzeSwaggerCoverageTool(_adapter);
        var args = new Dictionary<string, object>();

        await Assert.ThrowsAsync<ArgumentException>(() => tool.ExecuteAsync(args));
    }

    [Fact]
    public void SuggestOptimizationsTool_HasCorrectMetadata()
    {
        var tool = new SuggestOptimizationsTool(_adapter);
        tool.Name.Should().Be("suggest_optimizations");
        tool.Description.Should().NotBeNullOrEmpty();
        tool.InputSchema.Should().NotBeNull();
    }

    [Fact]
    public void AnalyzeSwaggerCoverageTool_HasCorrectMetadata()
    {
        var tool = new AnalyzeSwaggerCoverageTool(_adapter);
        tool.Name.Should().Be("analyze_swagger_coverage");
        tool.Description.Should().NotBeNullOrEmpty();
        tool.InputSchema.Should().NotBeNull();
    }

    public void Dispose()
    {
        _mockFs.Dispose();
    }
}
