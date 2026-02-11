using SphereIntegrationHub.MCP.Services.Integration;
using SphereIntegrationHub.MCP.Tools;
using Xunit;

namespace SphereIntegrationHub.MCP.Tests;

public class ToolTests
{
    private readonly string _projectRoot;
    private readonly SihServicesAdapter _adapter;

    public ToolTests()
    {
        _projectRoot = Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", "..", ".."
        ));

        _adapter = new SihServicesAdapter(_projectRoot);
    }

    [Fact]
    public async Task ListApiCatalogVersionsTool_ReturnsVersions()
    {
        var tool = new ListApiCatalogVersionsTool(_adapter);
        var result = await tool.ExecuteAsync(null);

        Assert.NotNull(result);
        var dict = result as dynamic;
        Assert.NotNull(dict);
    }

    [Fact]
    public async Task GetApiDefinitionsTool_RequiresVersion()
    {
        var tool = new GetApiDefinitionsTool(_adapter);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await tool.ExecuteAsync(null);
        });
    }

    [Fact]
    public async Task GetApiDefinitionsTool_ReturnsDefinitions()
    {
        var tool = new GetApiDefinitionsTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["version"] = "3.10"
        };

        var result = await tool.ExecuteAsync(args);

        Assert.NotNull(result);
        var dict = result as dynamic;
        Assert.NotNull(dict);
    }

    [Fact]
    public async Task ListAvailableWorkflowsTool_ReturnsWorkflows()
    {
        var tool = new ListAvailableWorkflowsTool(_adapter);
        var result = await tool.ExecuteAsync(null);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetPluginCapabilitiesTool_ReturnsCapabilities()
    {
        var tool = new GetPluginCapabilitiesTool(_adapter);
        var result = await tool.ExecuteAsync(null);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task ExplainValidationErrorTool_RequiresCategory()
    {
        var tool = new ExplainValidationErrorTool(_adapter);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await tool.ExecuteAsync(null);
        });
    }

    [Fact]
    public async Task ExplainValidationErrorTool_ProvidesExplanation()
    {
        var tool = new ExplainValidationErrorTool(_adapter);
        var args = new Dictionary<string, object>
        {
            ["errorCategory"] = "Stage",
            ["errorMessage"] = "Stage name is required"
        };

        var result = await tool.ExecuteAsync(args);

        Assert.NotNull(result);
        var dict = result as dynamic;
        Assert.NotNull(dict);
    }
}
