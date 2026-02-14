using SphereIntegrationHub.MCP.Core;
using SphereIntegrationHub.MCP.Models;
using SphereIntegrationHub.MCP.Services.Catalog;
using SphereIntegrationHub.MCP.Services.Generation;
using SphereIntegrationHub.MCP.Services.Integration;
using SphereIntegrationHub.MCP.Services.Semantic;
using System.Text.Json;

namespace SphereIntegrationHub.MCP.Tools;

/// <summary>
/// Analyzes endpoint dependencies to determine execution order
/// </summary>
[McpTool("analyze_endpoint_dependencies", "Analyzes which endpoints need to be called before the target endpoint", Category = "Semantic", Level = "L2")]
public sealed class AnalyzeEndpointDependenciesTool : IMcpTool
{
    private readonly SwaggerSemanticAnalyzer _analyzer;

    public AnalyzeEndpointDependenciesTool(SihServicesAdapter adapter)
    {
        _analyzer = new SwaggerSemanticAnalyzer(adapter);
    }

    public string Name => "analyze_endpoint_dependencies";
    public string Description => "Analyzes Swagger schemas to detect which endpoints need to be called before the target endpoint";

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
            apiName = new
            {
                type = "string",
                description = "Name of the API"
            },
            endpoint = new
            {
                type = "string",
                description = "Target endpoint path"
            },
            httpVerb = new
            {
                type = "string",
                description = "HTTP verb (GET, POST, PUT, DELETE, PATCH)"
            }
        },
        required = new[] { "version", "apiName", "endpoint", "httpVerb" }
    };

    public async Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var version = arguments?.GetValueOrDefault("version")?.ToString()
            ?? throw new ArgumentException("version is required");
        var apiName = arguments?.GetValueOrDefault("apiName")?.ToString()
            ?? throw new ArgumentException("apiName is required");
        var endpoint = arguments?.GetValueOrDefault("endpoint")?.ToString()
            ?? throw new ArgumentException("endpoint is required");
        var httpVerb = arguments?.GetValueOrDefault("httpVerb")?.ToString()
            ?? throw new ArgumentException("httpVerb is required");

        var dependencies = await _analyzer.AnalyzeDependenciesAsync(version, apiName, endpoint, httpVerb);
        return dependencies;
    }
}

/// <summary>
/// Infers data flow between multiple endpoints
/// </summary>
[McpTool("infer_data_flow", "Analyzes multiple endpoints and maps response fields to request fields", Category = "Semantic", Level = "L2")]
public sealed class InferDataFlowTool : IMcpTool
{
    private readonly SwaggerSemanticAnalyzer _analyzer;
    private readonly SwaggerReader _swaggerReader;

    public InferDataFlowTool(SihServicesAdapter adapter)
    {
        _analyzer = new SwaggerSemanticAnalyzer(adapter);
        _swaggerReader = new SwaggerReader(adapter);
    }

    public string Name => "infer_data_flow";
    public string Description => "Analyzes multiple endpoints and maps response fields to request fields, building a data flow graph";

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
            endpoints = new
            {
                type = "array",
                description = "List of endpoints to analyze",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        apiName = new { type = "string" },
                        endpoint = new { type = "string" },
                        httpVerb = new { type = "string" }
                    },
                    required = new[] { "apiName", "endpoint", "httpVerb" }
                }
            }
        },
        required = new[] { "version", "endpoints" }
    };

    public async Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var version = arguments?.GetValueOrDefault("version")?.ToString()
            ?? throw new ArgumentException("version is required");

        List<EndpointSpec> endpoints;
        if (arguments?.TryGetValue("endpoints", out var endpointsObj) == true)
        {
            if (endpointsObj is JsonElement jsonElement)
            {
                endpoints = JsonSerializer.Deserialize<List<EndpointSpec>>(jsonElement.GetRawText()) ?? [];
            }
            else
            {
                throw new ArgumentException("endpoints must be a valid array");
            }
        }
        else
        {
            throw new ArgumentException("endpoints is required");
        }

        if (endpoints.Count == 0)
        {
            throw new ArgumentException("At least one endpoint is required");
        }

        // Build data flow graph
        var stages = new List<DataFlowStage>();
        var connections = new List<DataFlowConnection>();
        var warnings = new List<string>();

        // First pass: extract inputs and outputs for each endpoint
        for (int i = 0; i < endpoints.Count; i++)
        {
            var ep = endpoints[i];
            var endpointInfo = await _swaggerReader.GetEndpointSchemaAsync(version, ep.ApiName, ep.Endpoint, ep.HttpVerb);
            if (endpointInfo == null)
            {
                warnings.Add($"Endpoint not found: {ep.HttpVerb} {ep.Endpoint} in {ep.ApiName}");
                continue;
            }

            var stageId = $"stage_{i + 1}";
            var inputs = new List<DataField>();
            var outputs = new List<DataField>();

            // Extract inputs
            foreach (var param in endpointInfo.PathParameters)
            {
                inputs.Add(new DataField
                {
                    Name = param.Name,
                    Type = param.Type,
                    Location = "path",
                    Required = param.Required
                });
            }

            foreach (var param in endpointInfo.QueryParameters)
            {
                inputs.Add(new DataField
                {
                    Name = param.Name,
                    Type = param.Type,
                    Location = "query",
                    Required = param.Required
                });
            }

            if (endpointInfo.BodySchema != null)
            {
                foreach (var field in endpointInfo.BodySchema.Fields)
                {
                    inputs.Add(new DataField
                    {
                        Name = field.Key,
                        Type = field.Value.Type,
                        Location = "body",
                        Required = endpointInfo.BodySchema.RequiredFields.Contains(field.Key)
                    });
                }
            }

            // Extract outputs from 200 response
            if (endpointInfo.Responses.TryGetValue(200, out var response) && response.Fields != null)
            {
                foreach (var field in response.Fields)
                {
                    outputs.Add(new DataField
                    {
                        Name = field.Key,
                        Type = field.Value.Type,
                        Location = "response",
                        Required = false
                    });
                }
            }

            stages.Add(new DataFlowStage
            {
                StageId = stageId,
                ApiName = ep.ApiName,
                Endpoint = ep.Endpoint,
                HttpVerb = ep.HttpVerb,
                Inputs = inputs,
                Outputs = outputs
            });
        }

        // Second pass: find connections between stages
        for (int toIdx = 0; toIdx < stages.Count; toIdx++)
        {
            var toStage = stages[toIdx];

            foreach (var input in toStage.Inputs.Where(i => i.Required))
            {
                // Look for matching outputs in previous stages
                for (int fromIdx = 0; fromIdx < toIdx; fromIdx++)
                {
                    var fromStage = stages[fromIdx];

                    foreach (var output in fromStage.Outputs)
                    {
                        var confidence = CalculateFieldMatchConfidence(input.Name, output.Name, input.Type, output.Type);
                        if (confidence > 0.3)
                        {
                            var binding = GenerateBinding(fromStage.StageId, output.Name, input.Location);
                            connections.Add(new DataFlowConnection
                            {
                                FromStage = fromStage.StageId,
                                FromField = output.Name,
                                ToStage = toStage.StageId,
                                ToField = input.Name,
                                ToLocation = input.Location,
                                Confidence = confidence,
                                Binding = binding
                            });
                        }
                    }
                }
            }
        }

        // Determine execution order (already in order from input)
        var executionOrder = stages.Select(s => s.StageId).ToList();

        return new DataFlowGraph
        {
            Version = version,
            Stages = stages,
            Connections = connections,
            ExecutionOrder = executionOrder,
            Warnings = warnings
        };
    }

    private static double CalculateFieldMatchConfidence(string inputName, string outputName, string inputType, string outputType)
    {
        var confidence = 0.0;

        // Exact match
        if (inputName.Equals(outputName, StringComparison.OrdinalIgnoreCase))
        {
            confidence += 0.5;
        }
        // Normalized match
        else if (NormalizeFieldName(inputName) == NormalizeFieldName(outputName))
        {
            confidence += 0.4;
        }
        // Partial match
        else if (inputName.Contains(outputName, StringComparison.OrdinalIgnoreCase) ||
                 outputName.Contains(inputName, StringComparison.OrdinalIgnoreCase))
        {
            confidence += 0.3;
        }

        // Type match
        if (inputType.Equals(outputType, StringComparison.OrdinalIgnoreCase))
        {
            confidence += 0.3;
        }

        return Math.Min(confidence, 1.0);
    }

    private static string NormalizeFieldName(string name)
    {
        return name.Replace("_", "").Replace("-", "").ToLowerInvariant();
    }

    private static string GenerateBinding(string fromStageId, string fieldName, string location)
    {
        return $"{{{{stage:{fromStageId}.output.{fieldName}}}}}";
    }

    private sealed class EndpointSpec
    {
        [System.Text.Json.Serialization.JsonPropertyName("apiName")]
        public string ApiName { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("endpoint")]
        public string Endpoint { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("httpVerb")]
        public string HttpVerb { get; set; } = "";
    }
}

/// <summary>
/// Generates a complete workflow from a natural language goal
/// </summary>
[McpTool("suggest_workflow_from_goal", "Generates a complete workflow YAML from a natural language goal", Category = "Semantic", Level = "L2")]
public sealed class SuggestWorkflowFromGoalTool : IMcpTool
{
    private readonly SwaggerSemanticAnalyzer _analyzer;
    private readonly ApiCatalogReader _catalogReader;
    private readonly SwaggerReader _swaggerReader;
    private readonly StageGenerator _stageGenerator;

    public SuggestWorkflowFromGoalTool(SihServicesAdapter adapter)
    {
        _analyzer = new SwaggerSemanticAnalyzer(adapter);
        _catalogReader = new ApiCatalogReader(adapter);
        _swaggerReader = new SwaggerReader(adapter);
        _stageGenerator = new StageGenerator(adapter);
    }

    public string Name => "suggest_workflow_from_goal";
    public string Description => "Generates a complete workflow YAML from a natural language goal using semantic analysis";

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
            goal = new
            {
                type = "string",
                description = "Natural language description of what you want to accomplish"
            },
            includeAuth = new
            {
                type = "boolean",
                description = "Whether to include authentication stage (default: true)"
            }
        },
        required = new[] { "version", "goal" }
    };

    public async Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var version = arguments?.GetValueOrDefault("version")?.ToString()
            ?? throw new ArgumentException("version is required");
        var goal = arguments?.GetValueOrDefault("goal")?.ToString()
            ?? throw new ArgumentException("goal is required");
        var includeAuth = arguments?.GetValueOrDefault("includeAuth") as bool? ?? true;

        // Simple keyword-based analysis
        var keywords = ExtractKeywords(goal);
        var actions = ExtractActions(goal);
        var entities = ExtractEntities(goal);

        // Find relevant APIs
        var allApis = await _catalogReader.GetApiDefinitionsAsync(version);
        var relevantEndpoints = new List<(EndpointInfo endpoint, double score)>();

        foreach (var api in allApis)
        {
            try
            {
                var endpoints = await _swaggerReader.GetEndpointsAsync(version, api.Name);

                foreach (var endpoint in endpoints)
                {
                    var score = CalculateRelevanceScore(endpoint, keywords, actions, entities);
                    if (score > 0.2)
                    {
                        relevantEndpoints.Add((endpoint, score));
                    }
                }
            }
            catch
            {
                // Skip APIs that can't be loaded
            }
        }

        // Sort by relevance
        var selectedEndpoints = relevantEndpoints
            .OrderByDescending(e => e.score)
            .Take(5)
            .Select(e => e.endpoint)
            .ToList();

        if (selectedEndpoints.Count == 0)
        {
            throw new InvalidOperationException("Could not find relevant endpoints for the goal");
        }

        // Build workflow stages
        var stages = new List<SuggestionStage>();
        var workflowName = GenerateWorkflowName(goal);
        var assumptions = new List<string>();
        var warnings = new List<string>();

        // Add authentication stage if requested
        if (includeAuth)
        {
            assumptions.Add("Workflow requires authentication - authentication stage should be added");
        }

        // Generate stages for selected endpoints
        for (int i = 0; i < selectedEndpoints.Count; i++)
        {
            var endpoint = selectedEndpoints[i];
            var stageName = $"stage_{i + 1}";

            stages.Add(new SuggestionStage
            {
                StageName = stageName,
                ApiName = endpoint.ApiName,
                Endpoint = endpoint.Endpoint,
                HttpVerb = endpoint.HttpVerb,
                Purpose = endpoint.Summary,
                DataBindings = []
            });
        }

        // Generate YAML
        var workflowYaml = GenerateWorkflowYaml(workflowName, goal, stages);

        var confidence = selectedEndpoints.Count >= 3 ? "high" : selectedEndpoints.Count >= 2 ? "medium" : "low";

        return new WorkflowSuggestion
        {
            Goal = goal,
            WorkflowName = workflowName,
            WorkflowYaml = workflowYaml,
            Stages = stages,
            RequiredApis = stages.Select(s => s.ApiName).Distinct().ToList(),
            Assumptions = assumptions,
            Warnings = warnings,
            Confidence = confidence
        };
    }

    private static List<string> ExtractKeywords(string goal)
    {
        var words = goal.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Where(w => w.Length > 3).ToList();
    }

    private static List<string> ExtractActions(string goal)
    {
        var lowerGoal = goal.ToLowerInvariant();
        var actions = new List<string>();

        if (lowerGoal.Contains("create") || lowerGoal.Contains("add") || lowerGoal.Contains("new"))
            actions.Add("create");
        if (lowerGoal.Contains("get") || lowerGoal.Contains("retrieve") || lowerGoal.Contains("fetch") || lowerGoal.Contains("search"))
            actions.Add("read");
        if (lowerGoal.Contains("update") || lowerGoal.Contains("modify") || lowerGoal.Contains("change"))
            actions.Add("update");
        if (lowerGoal.Contains("delete") || lowerGoal.Contains("remove"))
            actions.Add("delete");
        if (lowerGoal.Contains("list") || lowerGoal.Contains("all"))
            actions.Add("list");

        return actions;
    }

    private static List<string> ExtractEntities(string goal)
    {
        var commonEntities = new[] { "customer", "account", "order", "product", "user", "invoice", "payment", "transaction", "report" };
        return commonEntities.Where(e => goal.Contains(e, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private static double CalculateRelevanceScore(EndpointInfo endpoint, List<string> keywords, List<string> actions, List<string> entities)
    {
        var score = 0.0;

        // Check endpoint path and summary
        var endpointText = $"{endpoint.Endpoint} {endpoint.Summary}".ToLowerInvariant();

        // Entity matching
        foreach (var entity in entities)
        {
            if (endpointText.Contains(entity))
                score += 0.4;
        }

        // Action matching
        foreach (var action in actions)
        {
            var matches = action switch
            {
                "create" => endpoint.HttpVerb == "POST",
                "read" => endpoint.HttpVerb == "GET",
                "update" => endpoint.HttpVerb == "PUT" || endpoint.HttpVerb == "PATCH",
                "delete" => endpoint.HttpVerb == "DELETE",
                "list" => endpoint.HttpVerb == "GET" && !endpoint.PathParameters.Any(p => p.Name.Contains("id")),
                _ => false
            };

            if (matches)
                score += 0.3;
        }

        // Keyword matching
        foreach (var keyword in keywords)
        {
            if (endpointText.Contains(keyword))
                score += 0.1;
        }

        return Math.Min(score, 1.0);
    }

    private static string GenerateWorkflowName(string goal)
    {
        var words = goal.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(4);
        var name = string.Join("_", words).ToLowerInvariant();
        return System.Text.RegularExpressions.Regex.Replace(name, @"[^a-z0-9_]", "");
    }

    private static string GenerateWorkflowYaml(string name, string description, List<SuggestionStage> stages)
    {
        var yaml = $@"version: 3.11
id: {Guid.NewGuid():N}
name: {name}
description: {description}
output: true

input:
  - name: username
    type: Text
    required: true

stages:
";

        foreach (var stage in stages)
        {
            yaml += $@"  - name: {stage.StageName}
    kind: Endpoint
    apiRef: {stage.ApiName}
    endpoint: {stage.Endpoint}
    httpVerb: {stage.HttpVerb}
    expectedStatus: 200
    output:
      dto: ""{{{{response.body}}}}""
      http_status: ""{{{{response.status}}}}""

";
        }

        var endStageSource = stages.Count > 0 ? stages[0].StageName : "stage_1";
        yaml += $@"endStage:
  output:
    result: ""{{{{stage:{endStageSource}.output.dto}}}}""
";

        return yaml;
    }
}
