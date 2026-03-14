using SphereIntegrationHub.MCP.Models;
using SphereIntegrationHub.MCP.Services.Integration;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SphereIntegrationHub.MCP.Services.Validation;

/// <summary>
/// Service for validating workflows and stages
/// Wraps the CLI WorkflowValidator functionality
/// </summary>
public sealed class WorkflowValidatorService
{
    private readonly SihServicesAdapter _adapter;
    private readonly IDeserializer _yamlDeserializer;

    public WorkflowValidatorService(SihServicesAdapter adapter)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    /// <summary>
    /// Validates a workflow YAML file
    /// </summary>
    public async Task<ValidationResult> ValidateWorkflowAsync(string workflowPath)
    {
        if (!File.Exists(workflowPath))
        {
            return new ValidationResult
            {
                Valid = false,
                Errors = [new ValidationError
                {
                    Category = "File",
                    Message = $"Workflow file not found: {workflowPath}"
                }]
            };
        }

        try
        {
            var yamlContent = await File.ReadAllTextAsync(workflowPath);
            var workflow = _yamlDeserializer.Deserialize<Dictionary<string, object>>(yamlContent);

            var errors = new List<ValidationError>();
            var warnings = new List<ValidationWarning>();

            // Basic structure validation
            ValidateWorkflowStructure(workflow, errors);

            // Validate stages if present
            if (workflow.TryGetValue("stages", out var stagesObj) && stagesObj is List<object> stages)
            {
                ValidateStages(stages, errors, warnings);
            }

            return new ValidationResult
            {
                Valid = errors.Count == 0,
                Errors = errors,
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            return new ValidationResult
            {
                Valid = false,
                Errors = [new ValidationError
                {
                    Category = "Parse",
                    Message = $"Failed to parse workflow: {ex.Message}"
                }]
            };
        }
    }

    /// <summary>
    /// Validates a single stage definition
    /// </summary>
    public ValidationResult ValidateStage(Dictionary<string, object> stage)
    {
        var errors = new List<ValidationError>();
        var warnings = new List<ValidationWarning>();

        if (!stage.ContainsKey("name") || string.IsNullOrWhiteSpace(stage["name"]?.ToString()))
        {
            errors.Add(new ValidationError
            {
                Category = "Stage",
                Message = "Stage name is required"
            });
        }

        var stageType = GetStageKind(stage);
        if (string.IsNullOrWhiteSpace(stageType))
        {
            errors.Add(new ValidationError
            {
                Category = "Stage",
                Message = "Stage kind is required"
            });
        }
        else
        {
            if (!IsValidStageType(stageType))
            {
                errors.Add(new ValidationError
                {
                    Category = "Stage",
                    Field = "kind",
                    Message = $"Invalid stage kind: {stageType}",
                    Suggestion = "Valid kinds: Endpoint, Workflow"
                });
            }
        }

        // Kind-specific validation
        if (!string.IsNullOrWhiteSpace(stageType))
        {
            var stageName = stage.GetValueOrDefault("name")?.ToString() ?? "unknown";

            switch (stageType?.ToLowerInvariant())
            {
                case "endpoint":
                case "api":
                    ValidateApiStage(stage, stageName, errors, warnings);
                    break;
                case "workflow":
                case "workflow-ref":
                    ValidateWorkflowStage(stage, stageName, errors);
                    break;
            }
        }

        return new ValidationResult
        {
            Valid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings
        };
    }

    /// <summary>
    /// Plans workflow execution by analyzing stage dependencies
    /// </summary>
    public async Task<ExecutionPlan> PlanWorkflowExecutionAsync(string workflowPath)
    {
        if (!File.Exists(workflowPath))
        {
            throw new FileNotFoundException($"Workflow file not found: {workflowPath}");
        }

        var yamlContent = await File.ReadAllTextAsync(workflowPath);
        var workflow = _yamlDeserializer.Deserialize<Dictionary<string, object>>(yamlContent);

        var stages = new List<ExecutionStage>();
        if (workflow.TryGetValue("stages", out var stagesObj) && stagesObj is List<object> stageList)
        {
            for (int i = 0; i < stageList.Count; i++)
            {
                if (stageList[i] is Dictionary<object, object> stageDict)
                {
                    var stage = stageDict.ToDictionary(
                        kvp => kvp.Key.ToString() ?? "",
                        kvp => kvp.Value);

                    stages.Add(new ExecutionStage
                    {
                        Name = stage.GetValueOrDefault("name")?.ToString() ?? $"stage_{i}",
                        Type = GetStageKind(stage) ?? "unknown",
                        Order = i,
                        Dependencies = ExtractDependencies(stage)
                    });
                }
            }
        }

        return new ExecutionPlan
        {
            WorkflowName = workflow.GetValueOrDefault("name")?.ToString() ?? "unknown",
            TotalStages = stages.Count,
            Stages = stages,
            EstimatedDuration = EstimateDuration(stages)
        };
    }

    private static void ValidateWorkflowStructure(Dictionary<string, object> workflow, List<ValidationError> errors)
    {
        if (!workflow.ContainsKey("name") || string.IsNullOrWhiteSpace(workflow["name"]?.ToString()))
        {
            errors.Add(new ValidationError
            {
                Category = "Workflow",
                Field = "name",
                Message = "Workflow name is required"
            });
        }

        if (!workflow.ContainsKey("version"))
        {
            errors.Add(new ValidationError
            {
                Category = "Workflow",
                Field = "version",
                Message = "Workflow version is required"
            });
        }
    }

    private static void ValidateStages(List<object> stages, List<ValidationError> errors, List<ValidationWarning> warnings)
    {
        if (stages.Count == 0)
        {
            warnings.Add(new ValidationWarning
            {
                Category = "Workflow",
                Message = "Workflow has no stages defined"
            });
        }

        var stageNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var stageObj in stages)
        {
            if (stageObj is Dictionary<object, object> stageDict)
            {
                var stage = stageDict.ToDictionary(
                    kvp => kvp.Key.ToString() ?? "",
                    kvp => kvp.Value);

                var stageName = stage.GetValueOrDefault("name")?.ToString();
                if (!string.IsNullOrEmpty(stageName))
                {
                    if (!stageNames.Add(stageName))
                    {
                        errors.Add(new ValidationError
                        {
                            Category = "Stage",
                            Stage = stageName,
                            Message = $"Duplicate stage name: {stageName}"
                        });
                    }
                }
            }
        }
    }

    private static void ValidateApiStage(Dictionary<string, object> stage, string stageName, List<ValidationError> errors, List<ValidationWarning> warnings)
    {
        if (!stage.ContainsKey("apiRef") && !stage.ContainsKey("api"))
        {
            errors.Add(new ValidationError
            {
                Category = "Stage",
                Stage = stageName,
                Field = "apiRef",
                Message = "Endpoint stage requires 'apiRef' field"
            });
        }

        if (!stage.ContainsKey("endpoint"))
        {
            errors.Add(new ValidationError
            {
                Category = "Stage",
                Stage = stageName,
                Field = "endpoint",
                Message = "API stage requires 'endpoint' field"
            });
        }

        if (!stage.ContainsKey("httpVerb") && !stage.ContainsKey("verb"))
        {
            warnings.Add(new ValidationWarning
            {
                Category = "Stage",
                Message = $"Stage '{stageName}' does not specify httpVerb, will default to GET"
            });
        }
    }

    private static void ValidateWorkflowStage(Dictionary<string, object> stage, string stageName, List<ValidationError> errors)
    {
        if (!stage.ContainsKey("workflowRef"))
        {
            errors.Add(new ValidationError
            {
                Category = "Stage",
                Stage = stageName,
                Field = "workflowRef",
                Message = "Workflow stage requires 'workflowRef' field"
            });
        }
    }

    private static bool IsValidStageType(string? stageType)
    {
        if (string.IsNullOrWhiteSpace(stageType))
        {
            return false;
        }

        var validTypes = new[] { "endpoint", "workflow", "api", "workflow-ref" };
        return validTypes.Contains(stageType.ToLowerInvariant());
    }

    private static List<string> ExtractDependencies(Dictionary<string, object> stage)
    {
        var dependencies = new List<string>();

        // Extract dependencies from template tokens in various fields
        foreach (var (key, value) in stage)
        {
            if (value is string strValue)
            {
                var tokens = ExtractTemplateTokens(strValue);
                dependencies.AddRange(tokens);
            }
        }

        return dependencies.Distinct().ToList();
    }

    private static string? GetStageKind(Dictionary<string, object> stage)
    {
        if (stage.TryGetValue("kind", out var kindObj))
        {
            return kindObj?.ToString();
        }

        if (stage.TryGetValue("type", out var typeObj))
        {
            var legacy = typeObj?.ToString();
            return legacy?.ToLowerInvariant() switch
            {
                "api" => "Endpoint",
                "workflow-ref" => "Workflow",
                _ => legacy
            };
        }

        return null;
    }

    private static List<string> ExtractTemplateTokens(string value)
    {
        var tokens = new List<string>();
        var pattern = @"\{\{([^}]+)\}\}";
        var matches = System.Text.RegularExpressions.Regex.Matches(value, pattern);

        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            if (match.Groups.Count > 1)
            {
                var token = match.Groups[1].Value.Trim();
                // Extract stage name from tokens like "stages.stageName.output"
                if (token.StartsWith("stages."))
                {
                    var parts = token.Split('.');
                    if (parts.Length > 1)
                    {
                        tokens.Add(parts[1]);
                    }
                }
            }
        }

        return tokens;
    }

    private static string EstimateDuration(List<ExecutionStage> stages)
    {
        // Simple estimation: 1 second per API stage, 0.1 second per other stage
        var totalSeconds = stages.Sum(s => s.Type.Equals("api", StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.1);
        return $"{totalSeconds:F1}s";
    }
}

public sealed class ExecutionPlan
{
    public required string WorkflowName { get; init; }
    public required int TotalStages { get; init; }
    public required List<ExecutionStage> Stages { get; init; }
    public required string EstimatedDuration { get; init; }
}

public sealed class ExecutionStage
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required int Order { get; init; }
    public List<string> Dependencies { get; init; } = [];
}
