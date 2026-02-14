using SphereIntegrationHub.MCP.Core;
using SphereIntegrationHub.MCP.Services.Integration;
using SphereIntegrationHub.MCP.Services.Validation;
using System.Text.Json;
using System.Text;

namespace SphereIntegrationHub.MCP.Tools;

/// <summary>
/// Validates a complete workflow YAML file
/// </summary>
[McpTool("validate_workflow", "Validates a complete workflow YAML file", Category = "Validation", Level = "L1")]
public sealed class ValidateWorkflowTool : IMcpTool
{
    private readonly WorkflowValidatorService _validator;
    private readonly SihServicesAdapter _adapter;

    public ValidateWorkflowTool(SihServicesAdapter adapter)
    {
        _adapter = adapter;
        _validator = new WorkflowValidatorService(adapter);
    }

    public string Name => "validate_workflow";
    public string Description => "Validates a complete workflow YAML file for structure, syntax, and semantic correctness";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            workflowPath = new
            {
                type = "string",
                description = "Path to the workflow YAML file (absolute or relative to workflows directory)"
            },
            workflowYaml = new
            {
                type = "string",
                description = "Raw workflow YAML content (alternative to workflowPath)"
            }
        }
    };

    public async Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var workflowPath = arguments?.GetValueOrDefault("workflowPath")?.ToString();
        var workflowYaml = arguments?.GetValueOrDefault("workflowYaml")?.ToString();

        if (string.IsNullOrWhiteSpace(workflowPath) && string.IsNullOrWhiteSpace(workflowYaml))
        {
            throw new ArgumentException("workflowPath or workflowYaml is required");
        }

        string? tempPath = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(workflowYaml))
            {
                tempPath = WriteTemporaryWorkflow(workflowYaml);
                workflowPath = tempPath;
            }
            else if (!Path.IsPathRooted(workflowPath!))
            {
                workflowPath = Path.Combine(_adapter.WorkflowsPath, workflowPath!);
            }

            // Throw exception if file doesn't exist (for test compatibility)
            if (!File.Exists(workflowPath))
            {
                throw new FileNotFoundException($"Workflow file not found: {workflowPath}", workflowPath);
            }

            var result = await _validator.ValidateWorkflowAsync(workflowPath!);

            // Return as camelCase anonymous object for test compatibility
            return new
            {
                isValid = result.Valid,
                errors = result.Errors.Select(e => new
                {
                    category = e.Category,
                    stage = e.Stage,
                    field = e.Field,
                    message = e.Message,
                    suggestion = e.Suggestion,
                    location = e.Location
                }).ToList(),
                warnings = result.Warnings.Select(w => new
                {
                    category = w.Category,
                    message = w.Message,
                    suggestion = w.Suggestion
                }).ToList()
            };
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tempPath) && File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static string WriteTemporaryWorkflow(string workflowYaml)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"sih-mcp-{Guid.NewGuid():N}.workflow");
        File.WriteAllText(tempPath, workflowYaml, Encoding.UTF8);
        return tempPath;
    }
}

/// <summary>
/// Validates a single stage definition
/// </summary>
[McpTool("validate_stage", "Validates a single stage definition", Category = "Validation", Level = "L1")]
public sealed class ValidateStageTool : IMcpTool
{
    private readonly WorkflowValidatorService _validator;

    public ValidateStageTool(SihServicesAdapter adapter)
    {
        _validator = new WorkflowValidatorService(adapter);
    }

    public string Name => "validate_stage";
    public string Description => "Validates a single stage definition for correctness";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            stageDefinition = new
            {
                type = "object",
                description = "Stage definition as a JSON object"
            }
        },
        required = new[] { "stageDefinition" }
    };

    public Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var stageDefObj = arguments?.GetValueOrDefault("stageDefinition")
            ?? throw new ArgumentException("stageDefinition is required");

        // Convert the stage definition to Dictionary<string, object>
        Dictionary<string, object> stageDef;
        if (stageDefObj is JsonElement jsonElement)
        {
            var json = jsonElement.GetRawText();
            stageDef = JsonSerializer.Deserialize<Dictionary<string, object>>(json)
                ?? throw new ArgumentException("Failed to parse stageDefinition");
        }
        else if (stageDefObj is Dictionary<string, object> dict)
        {
            stageDef = dict;
        }
        else
        {
            throw new ArgumentException("stageDefinition must be a JSON object");
        }

        var result = _validator.ValidateStage(stageDef);

        // Return as camelCase anonymous object for test compatibility
        return Task.FromResult<object>(new
        {
            isValid = result.Valid,
            errors = result.Errors.Select(e => new
            {
                category = e.Category,
                stage = e.Stage,
                field = e.Field,
                message = e.Message,
                suggestion = e.Suggestion,
                location = e.Location
            }).ToList(),
            warnings = result.Warnings.Select(w => new
            {
                category = w.Category,
                message = w.Message,
                suggestion = w.Suggestion
            }).ToList()
        });
    }
}

/// <summary>
/// Plans workflow execution by analyzing dependencies
/// </summary>
[McpTool("plan_workflow_execution", "Plans workflow execution by analyzing stage dependencies", Category = "Validation", Level = "L1")]
public sealed class PlanWorkflowExecutionTool : IMcpTool
{
    private readonly WorkflowValidatorService _validator;
    private readonly SihServicesAdapter _adapter;

    public PlanWorkflowExecutionTool(SihServicesAdapter adapter)
    {
        _adapter = adapter;
        _validator = new WorkflowValidatorService(adapter);
    }

    public string Name => "plan_workflow_execution";
    public string Description => "Plans workflow execution by analyzing stage dependencies and execution order";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            workflowPath = new
            {
                type = "string",
                description = "Path to the workflow YAML file"
            },
            workflowYaml = new
            {
                type = "string",
                description = "Raw workflow YAML content (alternative to workflowPath)"
            }
        }
    };

    public async Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var workflowPath = arguments?.GetValueOrDefault("workflowPath")?.ToString();
        var workflowYaml = arguments?.GetValueOrDefault("workflowYaml")?.ToString();
        if (string.IsNullOrWhiteSpace(workflowPath) && string.IsNullOrWhiteSpace(workflowYaml))
        {
            throw new ArgumentException("workflowPath or workflowYaml is required");
        }

        string? tempPath = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(workflowYaml))
            {
                tempPath = WriteTemporaryWorkflow(workflowYaml);
                workflowPath = tempPath;
            }
            else if (!Path.IsPathRooted(workflowPath!))
            {
                workflowPath = Path.Combine(_adapter.WorkflowsPath, workflowPath!);
            }

            var plan = await _validator.PlanWorkflowExecutionAsync(workflowPath!);

            // Return as camelCase anonymous object for test compatibility
            return new
            {
                workflowName = plan.WorkflowName,
                totalStages = plan.TotalStages,
                executionOrder = plan.Stages.OrderBy(s => s.Order).Select(s => s.Name).ToList(),
                stages = plan.Stages.Select(s => new {
                    name = s.Name,
                    type = s.Type,
                    order = s.Order,
                    dependencies = s.Dependencies
                }).ToList(),
                estimatedDuration = plan.EstimatedDuration
            };
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tempPath) && File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static string WriteTemporaryWorkflow(string workflowYaml)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"sih-mcp-{Guid.NewGuid():N}.workflow");
        File.WriteAllText(tempPath, workflowYaml, Encoding.UTF8);
        return tempPath;
    }
}
