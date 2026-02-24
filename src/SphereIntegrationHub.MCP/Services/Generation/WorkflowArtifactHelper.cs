using SphereIntegrationHub.Definitions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SphereIntegrationHub.MCP.Services.Generation;

internal static class WorkflowArtifactHelper
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder().Build();

    public static string? GenerateWfvars(string workflowYaml)
    {
        var inputNames = ExtractInputNames(workflowYaml);
        if (inputNames.Count == 0)
        {
            return null;
        }

        var wfvars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var inputName in inputNames)
        {
            wfvars[inputName] = $"<set-{inputName}>";
        }

        return Serializer.Serialize(wfvars);
    }

    public static string? GenerateWfvarsDraft(string workflowYaml) => GenerateWfvars(workflowYaml);

    public static IReadOnlyList<string> ExtractInputNames(string workflowYaml)
    {
        if (string.IsNullOrWhiteSpace(workflowYaml))
        {
            return [];
        }

        var definition = Deserializer.Deserialize<WorkflowDefinition>(workflowYaml);
        if (definition?.Input == null || definition.Input.Count == 0)
        {
            return [];
        }

        return definition.Input
            .Select(x => x.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
