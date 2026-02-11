using SphereIntegrationHub.MCP.Core;
using SphereIntegrationHub.MCP.Models;
using SphereIntegrationHub.MCP.Services.Integration;
using SphereIntegrationHub.MCP.Services.Synthesis;
using System.Text.Json;

namespace SphereIntegrationHub.MCP.Tools;

/// <summary>
/// Synthesizes complete system designs from natural language descriptions
/// </summary>
[McpTool("synthesize_system_from_description", "Generates a complete system design with multiple workflows from a natural language description", Category = "Synthesis", Level = "L3")]
public sealed class SynthesizeSystemFromDescriptionTool : IMcpTool
{
    private readonly WorkflowSynthesizer _synthesizer;

    public SynthesizeSystemFromDescriptionTool(SihServicesAdapter adapter)
    {
        _synthesizer = new WorkflowSynthesizer(adapter);
    }

    public string Name => "synthesize_system_from_description";
    public string Description => "Full autonomous system generation from natural language description with dependency analysis and test scenarios";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            version = new
            {
                type = "string",
                description = "API catalog version"
            },
            description = new
            {
                type = "string",
                description = "Natural language description of the system to build"
            },
            requirements = new
            {
                type = "object",
                description = "System requirements and constraints",
                properties = new
                {
                    requiredApis = new
                    {
                        type = "array",
                        description = "APIs that must be used",
                        items = new { type = "string" }
                    },
                    preferredApis = new
                    {
                        type = "array",
                        description = "APIs that are preferred but not required",
                        items = new { type = "string" }
                    },
                    maxStagesPerWorkflow = new
                    {
                        type = "integer",
                        description = "Maximum stages per workflow (default: 10)"
                    },
                    includeAuthentication = new
                    {
                        type = "boolean",
                        description = "Include authentication workflow (default: true)"
                    },
                    includeErrorHandling = new
                    {
                        type = "boolean",
                        description = "Include error handling in test scenarios (default: true)"
                    },
                    includeRetries = new
                    {
                        type = "boolean",
                        description = "Include retry policies in workflows (default: true)"
                    },
                    excludeEndpoints = new
                    {
                        type = "array",
                        description = "Endpoints to exclude from consideration",
                        items = new { type = "string" }
                    },
                    performanceTarget = new
                    {
                        type = "string",
                        description = "Performance target: 'fast', 'balanced', or 'thorough' (default: 'balanced')"
                    }
                }
            }
        },
        required = new[] { "version", "description" }
    };

    public async Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var version = arguments?.GetValueOrDefault("version")?.ToString()
            ?? throw new ArgumentException("version is required");
        var description = arguments?.GetValueOrDefault("description")?.ToString()
            ?? throw new ArgumentException("description is required");

        // Parse requirements
        var requirements = new SystemRequirements
        {
            MaxStagesPerWorkflow = 10,
            IncludeAuthentication = true,
            IncludeErrorHandling = true,
            IncludeRetries = true,
            PerformanceTarget = "balanced"
        };

        if (arguments?.TryGetValue("requirements", out var reqObj) == true)
        {
            if (reqObj is JsonElement jsonElement)
            {
                requirements = ParseRequirements(jsonElement, requirements);
            }
        }

        // Synthesize the system
        var systemDesign = await _synthesizer.SynthesizeAsync(version, description, requirements);

        return new
        {
            description,
            version,
            workflows = systemDesign.Workflows.Select(w => new
            {
                w.Name,
                w.Path,
                w.Stages,
                w.Description,
                yamlPreview = w.Yaml.Length > 500 ? w.Yaml[..500] + "..." : w.Yaml,
                fullYaml = w.Yaml
            }).ToList(),
            dependencies = systemDesign.Dependencies,
            testScenarios = systemDesign.TestScenarios,
            apiUsage = systemDesign.ApiUsage.Select(kv => new
            {
                api = kv.Key,
                endpoints = kv.Value,
                count = kv.Value.Count
            }).ToList(),
            metrics = new
            {
                totalWorkflows = systemDesign.Workflows.Count,
                totalStages = systemDesign.Workflows.Sum(w => w.Stages),
                estimatedComplexity = systemDesign.EstimatedComplexity,
                estimatedExecutionTime = systemDesign.EstimatedExecutionTime,
                apisUsed = systemDesign.ApiUsage.Count
            },
            summary = GenerateSummary(systemDesign)
        };
    }

    private static SystemRequirements ParseRequirements(JsonElement jsonElement, SystemRequirements defaults)
    {
        var requirements = new SystemRequirements
        {
            MaxStagesPerWorkflow = defaults.MaxStagesPerWorkflow,
            IncludeAuthentication = defaults.IncludeAuthentication,
            IncludeErrorHandling = defaults.IncludeErrorHandling,
            IncludeRetries = defaults.IncludeRetries,
            PerformanceTarget = defaults.PerformanceTarget
        };

        if (jsonElement.TryGetProperty("requiredApis", out var requiredApis) &&
            requiredApis.ValueKind == JsonValueKind.Array)
        {
            requirements.RequiredApis.AddRange(
                JsonSerializer.Deserialize<List<string>>(requiredApis.GetRawText()) ?? []);
        }

        if (jsonElement.TryGetProperty("preferredApis", out var preferredApis) &&
            preferredApis.ValueKind == JsonValueKind.Array)
        {
            requirements.PreferredApis.AddRange(
                JsonSerializer.Deserialize<List<string>>(preferredApis.GetRawText()) ?? []);
        }

        if (jsonElement.TryGetProperty("maxStagesPerWorkflow", out var maxStages) &&
            maxStages.ValueKind == JsonValueKind.Number)
        {
            requirements.MaxStagesPerWorkflow = maxStages.GetInt32();
        }

        if (jsonElement.TryGetProperty("includeAuthentication", out var includeAuth) &&
            includeAuth.ValueKind == JsonValueKind.True || includeAuth.ValueKind == JsonValueKind.False)
        {
            requirements.IncludeAuthentication = includeAuth.GetBoolean();
        }

        if (jsonElement.TryGetProperty("includeErrorHandling", out var includeError) &&
            includeError.ValueKind == JsonValueKind.True || includeError.ValueKind == JsonValueKind.False)
        {
            requirements.IncludeErrorHandling = includeError.GetBoolean();
        }

        if (jsonElement.TryGetProperty("includeRetries", out var includeRetries) &&
            includeRetries.ValueKind == JsonValueKind.True || includeRetries.ValueKind == JsonValueKind.False)
        {
            requirements.IncludeRetries = includeRetries.GetBoolean();
        }

        if (jsonElement.TryGetProperty("excludeEndpoints", out var excludeEndpoints) &&
            excludeEndpoints.ValueKind == JsonValueKind.Array)
        {
            requirements.ExcludeEndpoints.AddRange(
                JsonSerializer.Deserialize<List<string>>(excludeEndpoints.GetRawText()) ?? []);
        }

        if (jsonElement.TryGetProperty("performanceTarget", out var perfTarget) &&
            perfTarget.ValueKind == JsonValueKind.String)
        {
            requirements.PerformanceTarget = perfTarget.GetString();
        }

        return requirements;
    }

    private static Dictionary<string, object> GenerateSummary(SystemDesign design)
    {
        var summary = new Dictionary<string, object>
        {
            ["workflowCount"] = design.Workflows.Count,
            ["totalStages"] = design.Workflows.Sum(w => w.Stages),
            ["averageStagesPerWorkflow"] = design.Workflows.Count > 0
                ? (double)design.Workflows.Sum(w => w.Stages) / design.Workflows.Count
                : 0,
            ["complexity"] = design.EstimatedComplexity,
            ["estimatedTime"] = design.EstimatedExecutionTime,
            ["apisInvolved"] = design.ApiUsage.Keys.ToList(),
            ["testScenariosGenerated"] = design.TestScenarios.Count,
            ["dependenciesDetected"] = design.Dependencies.Count
        };

        return summary;
    }
}
