using SphereIntegrationHub.Services;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SphereIntegrationHub.cli;

internal sealed class WorkflowConfigLoader : IWorkflowConfigLoader
{
    private readonly IDeserializer _deserializer;

    public WorkflowConfigLoader()
    {
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public WorkflowConfig Load(string workflowPath)
    {
        using var activity = Telemetry.ActivitySource.StartActivity(TelemetryConstants.ActivityWorkflowConfigLoad);
        activity?.SetTag(TelemetryConstants.TagWorkflowPath, workflowPath);

        if (string.IsNullOrWhiteSpace(workflowPath))
        {
            return new WorkflowConfig();
        }

        var directory = Path.GetDirectoryName(workflowPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return new WorkflowConfig();
        }

        var configPath = Path.IsPathRooted(CliConstants.WorkflowConfigFileName)
            ? CliConstants.WorkflowConfigFileName
            : Path.Combine(directory, CliConstants.WorkflowConfigFileName);
        activity?.SetTag(TelemetryConstants.TagFilePath, configPath);
        if (!File.Exists(configPath))
        {
            return new WorkflowConfig
            {
                ConfigPath = configPath
            };
        }

        var yaml = File.ReadAllText(configPath);
        var config = _deserializer.Deserialize<WorkflowConfig>(yaml);
        if (config is null)
        {
            return new WorkflowConfig
            {
                ConfigPath = configPath
            };
        }

        config.ConfigPath = configPath;
        return config;
    }
}
