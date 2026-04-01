using System.Text.Json;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SphereIntegrationHub.Services;

public sealed class WorkflowDataFileService
{
    private readonly IDeserializer _yamlDeserializer;

    public WorkflowDataFileService()
    {
        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
    }

    public string LoadText(string path, string workflowPath)
    {
        var resolvedPath = ResolvePath(path, workflowPath);
        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException("Workflow data file was not found.", resolvedPath);
        }

        return File.ReadAllText(resolvedPath);
    }

    public JsonElement LoadStructured(string path, string workflowPath)
    {
        var content = LoadText(path, workflowPath);
        var extension = Path.GetExtension(path);
        if (extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".yml", StringComparison.OrdinalIgnoreCase))
        {
            var yamlObject = _yamlDeserializer.Deserialize<object>(content);
            return JsonSerializer.SerializeToElement(yamlObject);
        }

        if (JsonValueHelper.TryParse(content, out var json))
        {
            return json;
        }

        var generic = _yamlDeserializer.Deserialize<object>(content);
        return JsonSerializer.SerializeToElement(generic);
    }

    private static string ResolvePath(string path, string workflowPath)
    {
        var baseDirectory = Path.GetDirectoryName(workflowPath) ?? string.Empty;
        return Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(baseDirectory, path));
    }
}
