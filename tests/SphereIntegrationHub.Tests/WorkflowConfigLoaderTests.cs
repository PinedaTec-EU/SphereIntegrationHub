using SphereIntegrationHub.cli;

namespace SphereIntegrationHub.Tests;

public sealed class WorkflowConfigLoaderTests
{
    [Fact]
    public void Load_WhenConfigMissing_CreatesDefaultWorkflowsConfig()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"aos-workflow-config-{Guid.NewGuid():N}");
        var workflowsPath = Path.Combine(tempRoot, "workflows");
        Directory.CreateDirectory(workflowsPath);
        var workflowPath = Path.Combine(workflowsPath, "sample.workflow");
        File.WriteAllText(workflowPath, "version: 0.1");

        var configPath = Path.Combine(workflowsPath, "workflows.config");
        if (File.Exists(configPath))
        {
            File.Delete(configPath);
        }

        var loader = new WorkflowConfigLoader();
        var config = loader.Load(workflowPath);

        Assert.NotNull(config);
        Assert.True(File.Exists(configPath));
        var yaml = File.ReadAllText(configPath);
        Assert.Contains("features:", yaml, StringComparison.Ordinal);
        Assert.Contains("openTelemetry: false", yaml, StringComparison.OrdinalIgnoreCase);
    }
}
