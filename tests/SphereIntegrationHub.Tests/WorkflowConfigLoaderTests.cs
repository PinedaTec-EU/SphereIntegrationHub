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
        Assert.Contains("reporting:", yaml, StringComparison.OrdinalIgnoreCase);
        Assert.True(config.Reporting.Enabled);
    }

    [Fact]
    public void Load_ResolvesReferencedEnvironmentFileFromEnvironmentVariable()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"aos-workflow-config-{Guid.NewGuid():N}");
        var workflowsPath = Path.Combine(tempRoot, "workflows");
        var envsPath = Path.Combine(workflowsPath, "envs");
        Directory.CreateDirectory(envsPath);

        var workflowPath = Path.Combine(workflowsPath, "sample.workflow");
        File.WriteAllText(workflowPath, """
version: "1.0"
id: "wf-1"
name: "sample"
references:
  environmentFile: "./envs/{{env:TENANT}}.env"
""");

        var envPath = Path.Combine(envsPath, "tenant-a.env");
        File.WriteAllText(envPath, "TENANT_NAME=tenant-a");

        var previous = Environment.GetEnvironmentVariable("TENANT");
        Environment.SetEnvironmentVariable("TENANT", "tenant-a");

        try
        {
            var loader = new SphereIntegrationHub.Services.WorkflowLoader();
            var document = loader.Load(workflowPath);

            Assert.Equal("tenant-a", document.EnvironmentVariables["TENANT_NAME"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TENANT", previous);
            Directory.Delete(tempRoot, true);
        }
    }
}
