namespace SphereIntegrationHub.Definitions;

public static class WorkflowConfigDefaults
{
    public const string FileName = "workflows.config";

    private const string DefaultConfigYaml = """
features:
  openTelemetry: false
openTelemetry:
  serviceName: "SphereIntegrationHub.cli"
  endpoint: "http://localhost:4317"
  consoleExporter: false
  debugConsole: false
""";

    public static void EnsureExists(string configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath) || File.Exists(configPath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(configPath, DefaultConfigYaml);
    }
}
