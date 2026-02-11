using SphereIntegrationHub.MCP.Core;
using SphereIntegrationHub.MCP.Models;
using SphereIntegrationHub.MCP.Services.Catalog;
using SphereIntegrationHub.MCP.Services.Integration;
using SphereIntegrationHub.MCP.Services.Optimization;
using System.Text.Json;

namespace SphereIntegrationHub.MCP.Tools;

/// <summary>
/// Analyzes workflows and suggests performance optimizations
/// </summary>
[McpTool("suggest_optimizations", "Analyzes a workflow and suggests performance and resilience optimizations", Category = "Optimization", Level = "L4")]
public sealed class SuggestOptimizationsTool : IMcpTool
{
    private readonly WorkflowOptimizer _optimizer;

    public SuggestOptimizationsTool(SihServicesAdapter adapter)
    {
        _optimizer = new WorkflowOptimizer();
    }

    public string Name => "suggest_optimizations";
    public string Description => "Analyzes workflow YAML and suggests improvements for parallelization, redundancy, resilience, caching, and batching";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            workflowYaml = new
            {
                type = "string",
                description = "The workflow YAML to analyze"
            },
            constraints = new
            {
                type = "object",
                description = "Optional constraints for optimization",
                properties = new
                {
                    maxParallelism = new
                    {
                        type = "integer",
                        description = "Maximum parallel stages allowed"
                    },
                    prioritizeSpeed = new
                    {
                        type = "boolean",
                        description = "Prioritize speed over other concerns"
                    },
                    prioritizeReliability = new
                    {
                        type = "boolean",
                        description = "Prioritize reliability over speed"
                    }
                }
            }
        },
        required = new[] { "workflowYaml" }
    };

    public Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var workflowYaml = arguments?.GetValueOrDefault("workflowYaml")?.ToString()
            ?? throw new ArgumentException("workflowYaml is required");

        Dictionary<string, object>? constraints = null;
        if (arguments?.TryGetValue("constraints", out var constraintsObj) == true)
        {
            if (constraintsObj is JsonElement jsonElement)
            {
                constraints = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonElement.GetRawText());
            }
        }

        var report = _optimizer.AnalyzeWorkflow(workflowYaml, constraints);

        return Task.FromResult<object>(new
        {
            workflow = report.Workflow,
            currentMetrics = new
            {
                stages = report.CurrentMetrics.Stages,
                estimatedDuration = report.CurrentMetrics.EstimatedDuration,
                httpCalls = report.CurrentMetrics.HttpCalls,
                sequentialCalls = report.CurrentMetrics.SequentialCalls,
                parallelizableCalls = report.CurrentMetrics.ParallelizableCalls
            },
            optimizations = report.Optimizations.Select(opt => new
            {
                type = opt.Type,
                priority = opt.Priority,
                stage = opt.Stage,
                stages = opt.Stages,
                reason = opt.Reason,
                impact = new
                {
                    durationReduction = opt.Impact.DurationReduction,
                    networkCallsReduction = opt.Impact.NetworkCallsReduction,
                    reliabilityImprovement = opt.Impact.ReliabilityImprovement,
                    estimatedNewDuration = opt.Impact.EstimatedNewDuration
                },
                implementation = opt.Implementation
            }).ToList(),
            projectedMetrics = new
            {
                stages = report.ProjectedMetrics.Stages,
                estimatedDuration = report.ProjectedMetrics.EstimatedDuration,
                httpCalls = report.ProjectedMetrics.HttpCalls,
                improvement = report.ProjectedMetrics.ImprovementSummary
            },
            summary = GenerateOptimizationSummary(report)
        });
    }

    private static Dictionary<string, object> GenerateOptimizationSummary(OptimizationReport report)
    {
        var byType = report.Optimizations
            .GroupBy(o => o.Type)
            .ToDictionary(g => g.Key, g => g.Count());

        var byPriority = report.Optimizations
            .GroupBy(o => o.Priority)
            .ToDictionary(g => g.Key, g => g.Count());

        return new Dictionary<string, object>
        {
            ["totalOptimizations"] = report.Optimizations.Count,
            ["byType"] = byType,
            ["byPriority"] = byPriority,
            ["estimatedImprovement"] = report.ProjectedMetrics.ImprovementSummary ?? "None",
            ["topRecommendations"] = report.Optimizations
                .Where(o => o.Priority == "high")
                .Select(o => o.Reason)
                .Take(3)
                .ToList()
        };
    }
}

/// <summary>
/// Analyzes endpoint usage across all workflows
/// </summary>
[McpTool("analyze_swagger_coverage", "Shows which API endpoints are used in workflows and suggests uses for unused endpoints", Category = "Optimization", Level = "L4")]
public sealed class AnalyzeSwaggerCoverageTool : IMcpTool
{
    private readonly SihServicesAdapter _adapter;
    private readonly ApiCatalogReader _catalogReader;
    private readonly SwaggerReader _swaggerReader;

    public AnalyzeSwaggerCoverageTool(SihServicesAdapter adapter)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _catalogReader = new ApiCatalogReader(adapter);
        _swaggerReader = new SwaggerReader(adapter);
    }

    public string Name => "analyze_swagger_coverage";
    public string Description => "Shows endpoint usage statistics and suggests use cases for unused endpoints";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            version = new
            {
                type = "string",
                description = "API catalog version"
            }
        },
        required = new[] { "version" }
    };

    public async Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var version = arguments?.GetValueOrDefault("version")?.ToString()
            ?? throw new ArgumentException("version is required");

        // Load all endpoints from all APIs
        var allApis = await _catalogReader.GetApiDefinitionsAsync(version);
        var allEndpoints = new List<EndpointInfo>();

        foreach (var api in allApis)
        {
            try
            {
                var endpoints = await _swaggerReader.GetEndpointsAsync(version, api.Name);
                allEndpoints.AddRange(endpoints);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[AnalyzeSwaggerCoverage] Error loading {api.Name}: {ex.Message}");
            }
        }

        // Load all workflows from samples directory
        var workflowsPath = _adapter.GetWorkflowsPath();
        var endpointUsage = new Dictionary<string, EndpointUsageInfo>();

        // Initialize usage tracking
        foreach (var endpoint in allEndpoints)
        {
            var key = $"{endpoint.ApiName}:{endpoint.HttpVerb}:{endpoint.Endpoint}";
            endpointUsage[key] = new EndpointUsageInfo
            {
                ApiName = endpoint.ApiName,
                Endpoint = endpoint.Endpoint,
                HttpVerb = endpoint.HttpVerb,
                Summary = endpoint.Summary,
                Tags = endpoint.Tags,
                UsageCount = 0,
                UsedInWorkflows = []
            };
        }

        // Scan workflows if directory exists
        if (Directory.Exists(workflowsPath))
        {
            var workflowFiles = Directory.GetFiles(workflowsPath, "*.yaml", SearchOption.AllDirectories);

            foreach (var workflowFile in workflowFiles)
            {
                try
                {
                    var workflowYaml = await File.ReadAllTextAsync(workflowFile);
                    var workflowName = Path.GetFileNameWithoutExtension(workflowFile);

                    // Simple text search for API endpoints in workflow
                    foreach (var endpoint in allEndpoints)
                    {
                        if (workflowYaml.Contains(endpoint.Endpoint) &&
                            workflowYaml.Contains(endpoint.ApiName))
                        {
                            var key = $"{endpoint.ApiName}:{endpoint.HttpVerb}:{endpoint.Endpoint}";
                            if (endpointUsage.TryGetValue(key, out var usage))
                            {
                                usage.UsageCount++;
                                usage.UsedInWorkflows.Add(workflowName);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[AnalyzeSwaggerCoverage] Error processing workflow {workflowFile}: {ex.Message}");
                }
            }
        }

        // Calculate coverage statistics
        var totalEndpoints = allEndpoints.Count;
        var coveredEndpoints = endpointUsage.Values.Count(u => u.UsageCount > 0);
        var unusedEndpoints = totalEndpoints - coveredEndpoints;
        var coveragePercentage = totalEndpoints > 0 ? (double)coveredEndpoints / totalEndpoints * 100 : 0;

        // Generate suggestions for unused endpoints
        var unusedSuggestions = endpointUsage.Values
            .Where(u => u.UsageCount == 0)
            .Select(u => new UnusedEndpointSuggestion
            {
                ApiName = u.ApiName,
                Endpoint = u.Endpoint,
                HttpVerb = u.HttpVerb,
                Summary = u.Summary,
                PossibleUseCases = GenerateUseCaseSuggestions(u),
                Tags = u.Tags
            })
            .OrderBy(s => s.ApiName)
            .ThenBy(s => s.Endpoint)
            .ToList();

        // API breakdown
        var apiBreakdown = allEndpoints
            .GroupBy(e => e.ApiName)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var apiEndpoints = g.ToList();
                    var apiCovered = apiEndpoints.Count(e =>
                    {
                        var key = $"{e.ApiName}:{e.HttpVerb}:{e.Endpoint}";
                        return endpointUsage.TryGetValue(key, out var usage) && usage.UsageCount > 0;
                    });
                    return new ApiCoverage
                    {
                        ApiName = g.Key,
                        TotalEndpoints = apiEndpoints.Count,
                        CoveredEndpoints = apiCovered,
                        CoveragePercentage = apiEndpoints.Count > 0 ? (double)apiCovered / apiEndpoints.Count * 100 : 0
                    };
                });

        // Return as camelCase anonymous object for test compatibility
        return new
        {
            version = version,
            totalEndpoints = totalEndpoints,
            coveredEndpoints = coveredEndpoints,
            coveragePercentage = coveragePercentage,
            endpointUsage = endpointUsage.Values
                .Where(u => u.UsageCount > 0)
                .Select(u => new
                {
                    apiName = u.ApiName,
                    endpoint = u.Endpoint,
                    httpVerb = u.HttpVerb,
                    usageCount = u.UsageCount,
                    usedInWorkflows = u.UsedInWorkflows
                })
                .OrderByDescending(u => u.usageCount)
                .ToList(),
            unusedEndpoints = endpointUsage.Values
                .Where(u => u.UsageCount == 0)
                .Select(u => new
                {
                    apiName = u.ApiName,
                    endpoint = u.Endpoint,
                    httpVerb = u.HttpVerb,
                    summary = u.Summary
                })
                .OrderBy(u => u.apiName)
                .ThenBy(u => u.endpoint)
                .ToList(),
            unusedSuggestions = unusedSuggestions.Take(20).Select(s => new
            {
                apiName = s.ApiName,
                endpoint = s.Endpoint,
                httpVerb = s.HttpVerb,
                summary = s.Summary,
                possibleUseCases = s.PossibleUseCases,
                tags = s.Tags
            }).ToList(),
            apiBreakdown = apiBreakdown.ToDictionary(
                kvp => kvp.Key,
                kvp => new
                {
                    apiName = kvp.Value.ApiName,
                    totalEndpoints = kvp.Value.TotalEndpoints,
                    coveredEndpoints = kvp.Value.CoveredEndpoints,
                    coveragePercentage = kvp.Value.CoveragePercentage
                })
        };
    }

    private static List<string> GenerateUseCaseSuggestions(EndpointUsageInfo endpoint)
    {
        var suggestions = new List<string>();

        var lowerSummary = endpoint.Summary.ToLowerInvariant();
        var lowerEndpoint = endpoint.Endpoint.ToLowerInvariant();

        // Based on HTTP verb
        switch (endpoint.HttpVerb)
        {
            case "GET":
                if (lowerEndpoint.Contains("search") || lowerSummary.Contains("search"))
                    suggestions.Add("Use in a search workflow to find specific records");
                else if (lowerEndpoint.Contains("{id}") || lowerEndpoint.Contains("{"))
                    suggestions.Add("Retrieve specific resource details after creation or list operation");
                else
                    suggestions.Add("List resources for reporting or selection UI");
                break;

            case "POST":
                suggestions.Add("Create new resource in a multi-step workflow");
                if (lowerSummary.Contains("batch") || lowerSummary.Contains("bulk"))
                    suggestions.Add("Batch create multiple resources efficiently");
                break;

            case "PUT":
            case "PATCH":
                suggestions.Add("Update resource after validation or approval");
                suggestions.Add("Modify resource based on business logic");
                break;

            case "DELETE":
                suggestions.Add("Cleanup or cascade delete operation");
                suggestions.Add("Cancel or remove resource in error handling flow");
                break;
        }

        // Based on tags
        foreach (var tag in endpoint.Tags)
        {
            var lowerTag = tag.ToLowerInvariant();
            if (lowerTag.Contains("report"))
                suggestions.Add($"Generate {tag} for analytics");
            else if (lowerTag.Contains("admin"))
                suggestions.Add($"Administrative operation for {tag}");
        }

        // Based on endpoint path
        if (lowerEndpoint.Contains("validate"))
            suggestions.Add("Validation step before processing");
        if (lowerEndpoint.Contains("approve"))
            suggestions.Add("Approval workflow step");
        if (lowerEndpoint.Contains("status"))
            suggestions.Add("Check status in polling workflow");

        return suggestions.Take(3).ToList();
    }

    private sealed class EndpointUsageInfo
    {
        public required string ApiName { get; init; }
        public required string Endpoint { get; init; }
        public required string HttpVerb { get; init; }
        public required string Summary { get; init; }
        public required List<string> Tags { get; init; }
        public int UsageCount { get; set; }
        public List<string> UsedInWorkflows { get; init; } = [];
    }
}
