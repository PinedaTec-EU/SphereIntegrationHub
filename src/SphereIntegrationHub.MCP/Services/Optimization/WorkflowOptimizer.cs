using SphereIntegrationHub.MCP.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SphereIntegrationHub.MCP.Services.Optimization;

/// <summary>
/// Analyzes workflows and suggests optimizations
/// </summary>
public sealed class WorkflowOptimizer
{
    private readonly IDeserializer _yamlDeserializer;

    public WorkflowOptimizer()
    {
        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    /// <summary>
    /// Analyzes a workflow and suggests optimizations
    /// </summary>
    public OptimizationReport AnalyzeWorkflow(string workflowYaml, Dictionary<string, object>? constraints = null)
    {
        // Parse workflow
        var workflow = _yamlDeserializer.Deserialize<Dictionary<string, object>>(workflowYaml);

        var stages = ExtractStages(workflow);
        var workflowName = workflow.GetValueOrDefault("name")?.ToString() ?? "unnamed";

        // Calculate current metrics
        var currentMetrics = CalculateMetrics(stages);

        // Find optimization opportunities
        var optimizations = new List<Models.Optimization>();

        // 1. Detect parallelization opportunities
        optimizations.AddRange(FindParallelizationOpportunities(stages));

        // 2. Detect redundant calls
        optimizations.AddRange(FindRedundantCalls(stages));

        // 3. Suggest resilience improvements
        optimizations.AddRange(SuggestResilienceImprovements(stages));

        // 4. Suggest caching opportunities
        optimizations.AddRange(SuggestCaching(stages));

        // 5. Suggest batching opportunities
        optimizations.AddRange(SuggestBatching(stages));

        // Calculate projected metrics after optimizations
        var projectedMetrics = ProjectMetrics(currentMetrics, optimizations);

        return new Models.OptimizationReport
        {
            Workflow = workflowName,
            CurrentMetrics = currentMetrics,
            Optimizations = optimizations,
            ProjectedMetrics = projectedMetrics
        };
    }

    /// <summary>
    /// Finds stages that can be executed in parallel
    /// </summary>
    private List<Models.Optimization> FindParallelizationOpportunities(List<WorkflowStage> stages)
    {
        var optimizations = new List<Models.Optimization>();

        // Build dependency map
        var dependencies = new Dictionary<string, List<string>>();
        foreach (var stage in stages)
        {
            dependencies[stage.Name] = ExtractStageDependencies(stage);
        }

        // Find stages with no dependencies on each other
        var parallelGroups = new List<List<string>>();
        var currentGroup = new List<string>();

        foreach (var stage in stages)
        {
            if (currentGroup.Count == 0)
            {
                currentGroup.Add(stage.Name);
                continue;
            }

            // Check if this stage depends on any in current group
            var dependsOnGroup = dependencies[stage.Name].Any(dep => currentGroup.Contains(dep));
            var groupDependsOnThis = currentGroup.Any(s => dependencies[s].Contains(stage.Name));

            if (!dependsOnGroup && !groupDependsOnThis)
            {
                currentGroup.Add(stage.Name);
            }
            else
            {
                if (currentGroup.Count > 1)
                {
                    parallelGroups.Add(new List<string>(currentGroup));
                }
                currentGroup.Clear();
                currentGroup.Add(stage.Name);
            }
        }

        if (currentGroup.Count > 1)
        {
            parallelGroups.Add(currentGroup);
        }

        // Create optimization suggestions for each parallel group
        foreach (var group in parallelGroups)
        {
            var estimatedSavings = (group.Count - 1) * 2; // Assume 2 seconds per stage

            optimizations.Add(new Models.Optimization
            {
                Type = "parallelization",
                Priority = "high",
                Stages = group,
                Reason = $"These {group.Count} stages have no data dependencies and can run in parallel",
                Impact = new Models.OptimizationImpact
                {
                    DurationReduction = $"~{estimatedSavings}s",
                    NetworkCallsReduction = 0
                },
                Implementation = new
                {
                    strategy = "parallel",
                    stages = group,
                    yaml = @"stages:
  - name: parallel_group
    type: parallel
    stages:
      " + string.Join("\n      ", group.Select(s => $"- {s}"))
                }
            });
        }

        return optimizations;
    }

    /// <summary>
    /// Finds redundant API calls
    /// </summary>
    private List<Models.Optimization> FindRedundantCalls(List<WorkflowStage> stages)
    {
        var optimizations = new List<Models.Optimization>();

        // Group by API + endpoint + verb
        var callGroups = stages
            .Where(s => s.Type == "api")
            .GroupBy(s => $"{s.Api}:{s.Endpoint}:{s.Verb}")
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var group in callGroups)
        {
            var stageNames = group.Select(s => s.Name).ToList();
            var calls = group.Count();
            var savings = (calls - 1) * 2;

            optimizations.Add(new Models.Optimization
            {
                Type = "redundancy",
                Priority = "medium",
                Stages = stageNames,
                Reason = $"Same endpoint called {calls} times - result can be reused",
                Impact = new Models.OptimizationImpact
                {
                    DurationReduction = $"~{savings}s",
                    NetworkCallsReduction = calls - 1
                },
                Implementation = new
                {
                    strategy = "cache_result",
                    firstCall = stageNames[0],
                    reuseIn = stageNames.Skip(1).ToList(),
                    suggestion = $"Store result from {stageNames[0]} and reuse in subsequent stages"
                }
            });
        }

        return optimizations;
    }

    /// <summary>
    /// Suggests resilience improvements
    /// </summary>
    private List<Models.Optimization> SuggestResilienceImprovements(List<WorkflowStage> stages)
    {
        var optimizations = new List<Models.Optimization>();

        foreach (var stage in stages.Where(s => s.Type == "api"))
        {
            // Check for missing retry policy on POST/PUT
            if ((stage.Verb == "POST" || stage.Verb == "PUT") && !stage.HasRetry)
            {
                optimizations.Add(new Models.Optimization
                {
                    Type = "resilience",
                    Priority = "medium",
                    Stage = stage.Name,
                    Reason = $"{stage.Verb} operation without retry policy - may fail on transient errors",
                    Impact = new Models.OptimizationImpact
                    {
                        ReliabilityImprovement = "Handles transient failures automatically"
                    },
                    Implementation = new
                    {
                        yaml = $@"{stage.Name}:
  retry:
    maxAttempts: 3
    backoff: exponential
    retryableErrors: [500, 502, 503, 504]"
                    }
                });
            }

            // Suggest circuit breaker for external APIs
            if (!stage.HasCircuitBreaker)
            {
                optimizations.Add(new Models.Optimization
                {
                    Type = "resilience",
                    Priority = "low",
                    Stage = stage.Name,
                    Reason = "No circuit breaker - repeated failures can cascade",
                    Impact = new Models.OptimizationImpact
                    {
                        ReliabilityImprovement = "Prevents cascade failures"
                    },
                    Implementation = new
                    {
                        yaml = $@"{stage.Name}:
  circuitBreaker:
    failureThreshold: 5
    timeout: 60s
    halfOpenAttempts: 2"
                    }
                });
            }
        }

        return optimizations;
    }

    /// <summary>
    /// Suggests caching opportunities
    /// </summary>
    private List<Models.Optimization> SuggestCaching(List<WorkflowStage> stages)
    {
        var optimizations = new List<Models.Optimization>();

        // Look for GET requests that could be cached
        foreach (var stage in stages.Where(s => s.Type == "api" && s.Verb == "GET"))
        {
            if (!stage.HasCache)
            {
                optimizations.Add(new Models.Optimization
                {
                    Type = "caching",
                    Priority = "low",
                    Stage = stage.Name,
                    Reason = "GET request - response could be cached to reduce latency",
                    Impact = new Models.OptimizationImpact
                    {
                        DurationReduction = "Up to 2s per cached call"
                    },
                    Implementation = new
                    {
                        yaml = $@"{stage.Name}:
  cache:
    enabled: true
    ttl: 300
    key: ""{{{{ input.cacheKey }}}}"""
                    }
                });
            }
        }

        return optimizations;
    }

    /// <summary>
    /// Suggests batching opportunities
    /// </summary>
    private List<Models.Optimization> SuggestBatching(List<WorkflowStage> stages)
    {
        var optimizations = new List<Models.Optimization>();

        // Look for repeated POST/PUT to same endpoint
        var postGroups = stages
            .Where(s => s.Type == "api" && (s.Verb == "POST" || s.Verb == "PUT"))
            .GroupBy(s => $"{s.Api}:{s.Endpoint}")
            .Where(g => g.Count() > 2)
            .ToList();

        foreach (var group in postGroups)
        {
            var stageNames = group.Select(s => s.Name).ToList();
            var count = group.Count();

            optimizations.Add(new Models.Optimization
            {
                Type = "batching",
                Priority = "high",
                Stages = stageNames,
                Reason = $"Making {count} separate calls to same endpoint - could batch",
                Impact = new Models.OptimizationImpact
                {
                    DurationReduction = $"~{(count - 1) * 2}s",
                    NetworkCallsReduction = count - 1
                },
                Implementation = new
                {
                    strategy = "batch_request",
                    endpoint = group.Key,
                    suggestion = "Use batch API if available, or combine into single call with array payload"
                }
            });
        }

        return optimizations;
    }

    private static List<WorkflowStage> ExtractStages(Dictionary<string, object> workflow)
    {
        var stages = new List<WorkflowStage>();

        if (!workflow.TryGetValue("stages", out var stagesObj))
        {
            return stages;
        }

        if (stagesObj is List<object> stageList)
        {
            foreach (var stageObj in stageList)
            {
                if (stageObj is Dictionary<object, object> stageDict)
                {
                    var stage = ParseStage(stageDict);
                    if (stage != null)
                    {
                        stages.Add(stage);
                    }
                }
            }
        }

        return stages;
    }

    private static WorkflowStage? ParseStage(Dictionary<object, object> stageDict)
    {
        var name = stageDict.GetValueOrDefault("name")?.ToString() ?? "";
        var type = stageDict.GetValueOrDefault("type")?.ToString() ?? "";

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(type))
        {
            return null;
        }

        var stage = new WorkflowStage
        {
            Name = name,
            Type = type,
            Api = stageDict.GetValueOrDefault("api")?.ToString() ?? "",
            Endpoint = stageDict.GetValueOrDefault("endpoint")?.ToString() ?? "",
            Verb = stageDict.GetValueOrDefault("verb")?.ToString() ?? "",
            HasRetry = stageDict.ContainsKey("retry"),
            HasCircuitBreaker = stageDict.ContainsKey("circuitBreaker"),
            HasCache = stageDict.ContainsKey("cache"),
            Body = stageDict.GetValueOrDefault("body"),
            PathParams = stageDict.GetValueOrDefault("pathParams"),
            QueryParams = stageDict.GetValueOrDefault("queryParams")
        };

        return stage;
    }

    private static List<string> ExtractStageDependencies(WorkflowStage stage)
    {
        var dependencies = new List<string>();

        // Look for template references to other stages
        var bodyStr = stage.Body?.ToString() ?? "";
        var pathStr = stage.PathParams?.ToString() ?? "";
        var queryStr = stage.QueryParams?.ToString() ?? "";

        var allText = $"{bodyStr} {pathStr} {queryStr}";

        // Simple regex to find stage references like {{ stages.stage_name.output }}
        var matches = System.Text.RegularExpressions.Regex.Matches(allText, @"stages\.(\w+)\.");
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            if (match.Groups.Count > 1)
            {
                dependencies.Add(match.Groups[1].Value);
            }
        }

        return dependencies.Distinct().ToList();
    }

    private static WorkflowMetrics CalculateMetrics(List<WorkflowStage> stages)
    {
        var httpCalls = stages.Count(s => s.Type == "api");
        var estimatedDuration = httpCalls * 2; // 2 seconds per call

        return new WorkflowMetrics
        {
            Stages = stages.Count,
            EstimatedDuration = $"{estimatedDuration}s",
            HttpCalls = httpCalls,
            SequentialCalls = httpCalls,
            ParallelizableCalls = 0
        };
    }

    private static WorkflowMetrics ProjectMetrics(WorkflowMetrics current, List<Models.Optimization> optimizations)
    {
        var totalSavings = 0;
        var networkSavings = 0;

        foreach (var opt in optimizations)
        {
            if (opt.Impact.DurationReduction != null)
            {
                var match = System.Text.RegularExpressions.Regex.Match(opt.Impact.DurationReduction, @"(\d+)");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var savings))
                {
                    totalSavings += savings;
                }
            }

            if (opt.Impact.NetworkCallsReduction.HasValue)
            {
                networkSavings += opt.Impact.NetworkCallsReduction.Value;
            }
        }

        var currentDuration = int.Parse(current.EstimatedDuration.TrimEnd('s'));
        var newDuration = Math.Max(1, currentDuration - totalSavings);

        var improvementPct = totalSavings > 0
            ? $"{(double)totalSavings / currentDuration * 100:F0}% faster"
            : "No significant improvement";

        return new WorkflowMetrics
        {
            Stages = current.Stages,
            EstimatedDuration = $"{newDuration}s",
            HttpCalls = current.HttpCalls - networkSavings,
            SequentialCalls = current.SequentialCalls,
            ParallelizableCalls = 0,
            ImprovementSummary = improvementPct
        };
    }

    private sealed class WorkflowStage
    {
        public required string Name { get; init; }
        public required string Type { get; init; }
        public required string Api { get; init; }
        public required string Endpoint { get; init; }
        public required string Verb { get; init; }
        public bool HasRetry { get; init; }
        public bool HasCircuitBreaker { get; init; }
        public bool HasCache { get; init; }
        public object? Body { get; init; }
        public object? PathParams { get; init; }
        public object? QueryParams { get; init; }
    }
}
