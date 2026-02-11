using SphereIntegrationHub.MCP.Core;
using SphereIntegrationHub.MCP.Services.Integration;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SphereIntegrationHub.MCP.Tools;

/// <summary>
/// Lists all available workflows in the project
/// </summary>
[McpTool("list_available_workflows", "Lists all available workflow files in the project", Category = "Reference", Level = "L1")]
public sealed class ListAvailableWorkflowsTool : IMcpTool
{
    private readonly SihServicesAdapter _adapter;

    public ListAvailableWorkflowsTool(SihServicesAdapter adapter)
    {
        _adapter = adapter;
    }

    public string Name => "list_available_workflows";
    public string Description => "Lists all available workflow files in the project with their basic information";

    public object InputSchema => new
    {
        type = "object",
        properties = new { },
        required = Array.Empty<string>()
    };

    public Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        if (!Directory.Exists(_adapter.WorkflowsPath))
        {
            return Task.FromResult<object>(new
            {
                workflows = Array.Empty<object>(),
                count = 0,
                message = "Workflows directory not found"
            });
        }

        var workflowFiles = Directory.GetFiles(_adapter.WorkflowsPath, "*.workflow", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(_adapter.WorkflowsPath, "*.yaml", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(_adapter.WorkflowsPath, "*.yml", SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var workflows = new List<object>();
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        foreach (var file in workflowFiles)
        {
            try
            {
                var content = File.ReadAllText(file);
                var workflow = deserializer.Deserialize<Dictionary<string, object>>(content);

                workflows.Add(new
                {
                    path = Path.GetRelativePath(_adapter.WorkflowsPath, file),
                    absolutePath = file,
                    name = workflow.GetValueOrDefault("name")?.ToString() ?? Path.GetFileNameWithoutExtension(file),
                    version = workflow.GetValueOrDefault("version")?.ToString() ?? "unknown",
                    description = workflow.GetValueOrDefault("description")?.ToString() ?? "",
                    stageCount = workflow.TryGetValue("stages", out var stages) && stages is List<object> list ? list.Count : 0
                });
            }
            catch (Exception ex)
            {
                workflows.Add(new
                {
                    path = Path.GetRelativePath(_adapter.WorkflowsPath, file),
                    absolutePath = file,
                    name = Path.GetFileNameWithoutExtension(file),
                    error = $"Failed to parse: {ex.Message}"
                });
            }
        }

        return Task.FromResult<object>(new
        {
            workflows,
            count = workflows.Count,
            workflowsPath = _adapter.WorkflowsPath
        });
    }
}

/// <summary>
/// Gets inputs and outputs for a workflow
/// </summary>
[McpTool("get_workflow_inputs_outputs", "Gets the input parameters and output schema for a workflow", Category = "Reference", Level = "L1")]
public sealed class GetWorkflowInputsOutputsTool : IMcpTool
{
    private readonly SihServicesAdapter _adapter;

    public GetWorkflowInputsOutputsTool(SihServicesAdapter adapter)
    {
        _adapter = adapter;
    }

    public string Name => "get_workflow_inputs_outputs";
    public string Description => "Gets the input parameters and output schema for a workflow";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            workflowPath = new
            {
                type = "string",
                description = "Path to the workflow file (.workflow, .yaml, .yml)"
            }
        },
        required = new[] { "workflowPath" }
    };

    public async Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var workflowPath = arguments?.GetValueOrDefault("workflowPath")?.ToString()
            ?? throw new ArgumentException("workflowPath is required");

        // If path is not absolute, resolve relative to workflows directory
        if (!Path.IsPathRooted(workflowPath))
        {
            workflowPath = Path.Combine(_adapter.WorkflowsPath, workflowPath);
        }

        if (!File.Exists(workflowPath))
        {
            throw new FileNotFoundException($"Workflow file not found: {workflowPath}");
        }

        var content = await File.ReadAllTextAsync(workflowPath);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var workflow = deserializer.Deserialize<Dictionary<string, object>>(content);

        // Extract inputs
        var inputs = new List<object>();
        if (workflow.TryGetValue("input", out var inputObj) && inputObj is List<object> inputList)
        {
            foreach (var input in inputList)
            {
                if (input is Dictionary<object, object> inputDict)
                {
                    var inputData = inputDict.ToDictionary(
                        kvp => kvp.Key.ToString() ?? "",
                        kvp => kvp.Value);

                    inputs.Add(new
                    {
                        name = inputData.GetValueOrDefault("name")?.ToString() ?? "",
                        type = inputData.GetValueOrDefault("type")?.ToString() ?? "string",
                        required = inputData.GetValueOrDefault("required")?.ToString()?.ToLowerInvariant() == "true",
                        description = inputData.GetValueOrDefault("description")?.ToString() ?? ""
                    });
                }
            }
        }

        // Extract outputs from end-stage
        var outputs = new Dictionary<string, object>();
        if (workflow.TryGetValue("end-stage", out var endStageObj) && endStageObj is Dictionary<object, object> endStageDict)
        {
            var endStage = endStageDict.ToDictionary(
                kvp => kvp.Key.ToString() ?? "",
                kvp => kvp.Value);

            if (endStage.TryGetValue("output", out var outputObj) && outputObj is Dictionary<object, object> outputDict)
            {
                outputs = outputDict.ToDictionary(
                    kvp => kvp.Key.ToString() ?? "",
                    kvp => kvp.Value);
            }
        }

        return new
        {
            workflowName = workflow.GetValueOrDefault("name")?.ToString() ?? "unknown",
            inputs = inputs,
            inputCount = inputs.Count,
            outputs = outputs,
            outputCount = outputs.Count
        };
    }
}
