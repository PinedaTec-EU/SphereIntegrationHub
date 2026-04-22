using SphereIntegrationHub.cli;
using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Plugins;

namespace SphereIntegrationHub.Tests;

public sealed class StagePluginRegistryBuilderTests
{
    [Fact]
    public void Build_UsesHttpPluginByDefaultWhenPluginsSectionIsMissing()
    {
        var builder = new StagePluginRegistryBuilder();
        var config = new WorkflowConfig();
        var catalogVersion = new ApiCatalogVersion
        {
            Version = "1.0",
            Definitions = new List<ApiDefinition>()
        };

        var registry = builder.Build(config, catalogVersion, "/tmp/sample.workflow");

        Assert.True(registry.TryGetByKind(WorkflowStageKind.Endpoint, out var plugin));
        Assert.Equal("http", plugin.Descriptor.Id);
    }

    [Fact]
    public void Build_ThrowsWhenExplicitPluginIsNotDeclaredInCatalog()
    {
        var builder = new StagePluginRegistryBuilder();
        var config = new WorkflowConfig
        {
            Plugins = ["amqp"]
        };
        var catalogVersion = new ApiCatalogVersion
        {
            Version = "1.0",
            Definitions = new List<ApiDefinition>()
        };

        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build(config, catalogVersion, "/tmp/sample.workflow"));

        Assert.Contains("not declared in api.catalog", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
