using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.cli;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SphereIntegrationHub.Sdk.Internal;

internal sealed class SdkWorkflowConfigLoader
{
    private readonly IDeserializer _deserializer;

    public SdkWorkflowConfigLoader()
    {
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public WorkflowConfig Load(string workflowPath)
    {
        if (string.IsNullOrWhiteSpace(workflowPath))
        {
            return new WorkflowConfig();
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(workflowPath));
        if (string.IsNullOrWhiteSpace(directory))
        {
            return new WorkflowConfig();
        }

        var configPath = Path.Combine(directory, WorkflowConfigDefaults.FileName);
        WorkflowConfigDefaults.EnsureExists(configPath);

        var yaml = File.ReadAllText(configPath);
        return _deserializer.Deserialize<WorkflowConfig>(yaml) ?? new WorkflowConfig();
    }
}
