using System.Text.Json;

using SphereIntegrationHub.Definitions;
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
        var resolvedPath = WorkflowReferencePathResolver.ResolvePath(path, workflowPath);
        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException("Workflow data file was not found.", resolvedPath);
        }

        return File.ReadAllText(resolvedPath);
    }

    public string LoadText(string path, TemplateContext templateContext)
    {
        var resolvedPath = WorkflowReferencePathResolver.ResolvePath(path, templateContext);
        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException("Workflow data file was not found.", resolvedPath);
        }

        return File.ReadAllText(resolvedPath);
    }

    public JsonElement LoadStructured(string path, string workflowPath)
    {
        var content = LoadText(path, workflowPath);
        return ParseStructured(content, path);
    }

    public JsonElement ParseStructured(string content, string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(WorkflowConstants.ExtYaml, StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(WorkflowConstants.ExtYml, StringComparison.OrdinalIgnoreCase))
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
}
