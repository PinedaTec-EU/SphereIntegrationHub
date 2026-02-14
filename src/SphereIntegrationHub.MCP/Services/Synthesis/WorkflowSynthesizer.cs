using SphereIntegrationHub.MCP.Models;
using SphereIntegrationHub.MCP.Services.Catalog;
using SphereIntegrationHub.MCP.Services.Integration;
using SphereIntegrationHub.MCP.Services.Semantic;

namespace SphereIntegrationHub.MCP.Services.Synthesis;

/// <summary>
/// Synthesizes complete system designs from natural language descriptions
/// </summary>
public sealed class WorkflowSynthesizer
{
    private readonly SihServicesAdapter _adapter;
    private readonly IntentAnalyzer _intentAnalyzer;
    private readonly SwaggerSemanticAnalyzer _semanticAnalyzer;
    private readonly WorkflowGraphBuilder _graphBuilder;
    private readonly ApiCatalogReader _catalogReader;
    private readonly SwaggerReader _swaggerReader;

    public WorkflowSynthesizer(SihServicesAdapter adapter)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _intentAnalyzer = new IntentAnalyzer();
        _semanticAnalyzer = new SwaggerSemanticAnalyzer(adapter);
        _graphBuilder = new WorkflowGraphBuilder();
        _catalogReader = new ApiCatalogReader(adapter);
        _swaggerReader = new SwaggerReader(adapter);
    }

    /// <summary>
    /// Synthesizes a complete system design from a description
    /// </summary>
    public async Task<SystemDesign> SynthesizeAsync(
        string version,
        string description,
        SystemRequirements requirements)
    {
        // Step 1: Parse the description
        var intent = _intentAnalyzer.ParseDescription(description);

        // Step 2: Find relevant APIs and endpoints
        var relevantEndpoints = await FindRelevantEndpointsAsync(version, intent, requirements);

        if (relevantEndpoints.Count == 0)
        {
            throw new InvalidOperationException("Could not find any relevant endpoints for the description");
        }

        // Step 3: Analyze dependencies between endpoints
        var endpointDependencies = new List<EndpointDependencies>();
        foreach (var endpoint in relevantEndpoints)
        {
            try
            {
                var deps = await _semanticAnalyzer.AnalyzeDependenciesAsync(
                    version, endpoint.ApiName, endpoint.Endpoint, endpoint.HttpVerb);
                endpointDependencies.Add(deps);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WorkflowSynthesizer] Error analyzing {endpoint.Endpoint}: {ex.Message}");
            }
        }

        // Step 4: Build dependency graph
        var graph = _graphBuilder.BuildGraph(endpointDependencies);

        // Step 5: Partition into workflows (max stages per workflow)
        var workflows = PartitionIntoWorkflows(graph, endpointDependencies, requirements.MaxStagesPerWorkflow);

        // Step 6: Generate workflow YAMLs
        var workflowDesigns = new List<WorkflowDesign>();
        var workflowIndex = 1;

        foreach (var workflowEndpoints in workflows)
        {
            var workflowName = $"workflow_{workflowIndex}";
            var yaml = GenerateWorkflowYaml(
                workflowName,
                description,
                workflowEndpoints,
                requirements);

            workflowDesigns.Add(new WorkflowDesign
            {
                Name = workflowName,
                Path = $"workflows/{workflowName}.yaml",
                Yaml = yaml,
                Stages = workflowEndpoints.Count,
                Description = $"Part {workflowIndex} of {workflows.Count}"
            });

            workflowIndex++;
        }

        // Step 7: Add authentication workflow if needed
        if (requirements.IncludeAuthentication)
        {
            var authWorkflow = GenerateAuthenticationWorkflow();
            workflowDesigns.Insert(0, authWorkflow);
        }

        // Step 8: Generate dependencies between workflows
        var dependencies = GenerateWorkflowDependencies(workflowDesigns);

        // Step 9: Generate test scenarios
        var testScenarios = GenerateTestScenarios(workflowDesigns, requirements);

        // Step 10: Calculate metrics
        var apiUsage = relevantEndpoints
            .GroupBy(e => e.ApiName)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => $"{e.HttpVerb} {e.Endpoint}").ToList());

        var estimatedComplexity = intent.Complexity;
        var estimatedTime = EstimateExecutionTime(workflowDesigns);

        return new SystemDesign
        {
            Workflows = workflowDesigns,
            Dependencies = dependencies,
            TestScenarios = testScenarios,
            ApiUsage = apiUsage,
            EstimatedComplexity = estimatedComplexity,
            EstimatedExecutionTime = estimatedTime
        };
    }

    /// <summary>
    /// Finds endpoints relevant to the parsed intent
    /// </summary>
    private async Task<List<EndpointInfo>> FindRelevantEndpointsAsync(
        string version,
        ParsedIntent intent,
        SystemRequirements requirements)
    {
        var allApis = await _catalogReader.GetApiDefinitionsAsync(version);
        var relevantEndpoints = new List<(EndpointInfo endpoint, double score)>();

        // Filter APIs based on requirements
        var apisToSearch = allApis;
        if (requirements.RequiredApis.Any())
        {
            apisToSearch = allApis.Where(a => requirements.RequiredApis.Contains(a.Name)).ToList();
        }
        else if (requirements.PreferredApis.Any())
        {
            // Prefer specified APIs but include others
            apisToSearch = allApis
                .OrderByDescending(a => requirements.PreferredApis.Contains(a.Name) ? 1 : 0)
                .ToList();
        }

        foreach (var api in apisToSearch)
        {
            try
            {
                var endpoints = await _swaggerReader.GetEndpointsAsync(version, api.Name);

                foreach (var endpoint in endpoints)
                {
                    // Skip excluded endpoints
                    if (requirements.ExcludeEndpoints.Contains(endpoint.Endpoint))
                        continue;

                    var score = CalculateRelevanceScore(endpoint, intent);
                    if (score > 0.2)
                    {
                        relevantEndpoints.Add((endpoint, score));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WorkflowSynthesizer] Error loading {api.Name}: {ex.Message}");
            }
        }

        // Return top endpoints based on performance target
        var limit = requirements.PerformanceTarget switch
        {
            "fast" => 10,
            "thorough" => 30,
            _ => 20 // balanced
        };

        return relevantEndpoints
            .OrderByDescending(e => e.score)
            .Take(limit)
            .Select(e => e.endpoint)
            .ToList();
    }

    /// <summary>
    /// Calculates how relevant an endpoint is to the intent
    /// </summary>
    private static double CalculateRelevanceScore(EndpointInfo endpoint, ParsedIntent intent)
    {
        var score = 0.0;
        var endpointText = $"{endpoint.Endpoint} {endpoint.Summary} {endpoint.Description}".ToLowerInvariant();

        // Entity matching
        foreach (var entity in intent.Entities)
        {
            if (endpointText.Contains(entity))
                score += 0.3;
        }

        // Action matching
        foreach (var action in intent.Actions)
        {
            var matches = action switch
            {
                "create" => endpoint.HttpVerb == "POST",
                "read" => endpoint.HttpVerb == "GET" && endpoint.PathParameters.Any(p => p.Name.Contains("id")),
                "update" => endpoint.HttpVerb == "PUT" || endpoint.HttpVerb == "PATCH",
                "delete" => endpoint.HttpVerb == "DELETE",
                "list" => endpoint.HttpVerb == "GET" && !endpoint.PathParameters.Any(p => p.Name.Contains("id")),
                _ => false
            };

            if (matches)
                score += 0.4;

            // Also check if action word appears in endpoint
            if (endpointText.Contains(action))
                score += 0.2;
        }

        // Keyword matching
        foreach (var keyword in intent.Keywords)
        {
            if (endpointText.Contains(keyword))
                score += 0.1;
        }

        // Tag matching
        foreach (var tag in endpoint.Tags)
        {
            if (intent.Entities.Any(e => tag.Contains(e, StringComparison.OrdinalIgnoreCase)))
                score += 0.2;
        }

        return Math.Min(score, 1.0);
    }

    /// <summary>
    /// Partitions endpoints into workflows with max stages
    /// </summary>
    private static List<List<EndpointDependencies>> PartitionIntoWorkflows(
        DependencyGraph graph,
        List<EndpointDependencies> allEndpoints,
        int maxStagesPerWorkflow)
    {
        var workflows = new List<List<EndpointDependencies>>();
        var currentWorkflow = new List<EndpointDependencies>();

        // Use topological order to maintain dependencies
        foreach (var nodeId in graph.TopologicalOrder)
        {
            var endpoint = allEndpoints.FirstOrDefault(e =>
                nodeId.Contains(e.ApiName) && nodeId.Contains(e.Endpoint.Replace("/", "_")));

            if (endpoint != null)
            {
                if (currentWorkflow.Count >= maxStagesPerWorkflow)
                {
                    workflows.Add(currentWorkflow);
                    currentWorkflow = new List<EndpointDependencies>();
                }

                currentWorkflow.Add(endpoint);
            }
        }

        if (currentWorkflow.Any())
        {
            workflows.Add(currentWorkflow);
        }

        return workflows;
    }

    /// <summary>
    /// Generates workflow YAML from endpoints
    /// </summary>
    private static string GenerateWorkflowYaml(
        string name,
        string description,
        List<EndpointDependencies> endpoints,
        SystemRequirements requirements)
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

        for (int i = 0; i < endpoints.Count; i++)
        {
            var endpoint = endpoints[i];
            var stageName = $"stage_{i + 1}";

            yaml += $@"  - name: {stageName}
    kind: Endpoint
    apiRef: {endpoint.ApiName}
    endpoint: {endpoint.Endpoint}
    httpVerb: {endpoint.HttpVerb}
    expectedStatus: 200
";

            // Add retry policy if configured
            if (requirements.IncludeRetries && (endpoint.HttpVerb == "POST" || endpoint.HttpVerb == "PUT"))
            {
                yaml += @"    retry:
      maxAttempts: 3
      backoff: exponential
";
            }

            yaml += @"    output:
      dto: ""{{response.body}}""
      http_status: ""{{response.status}}""

";
        }

        yaml += @"endStage:
  output:
    success: ""true""
    result: ""{{stage:stage_1.output.dto}}""
";

        return yaml;
    }

    /// <summary>
    /// Generates authentication workflow
    /// </summary>
    private static WorkflowDesign GenerateAuthenticationWorkflow()
    {
        var yaml = @"version: 3.11
id: AUTHWORKFLOW00000000000000000000
name: authentication
description: OAuth authentication workflow
output: true

input:
  - name: clientId
    type: Text
    required: true
  - name: clientSecret
    type: Text
    required: true

stages:
  - name: get_token
    kind: Endpoint
    apiRef: auth-api
    endpoint: /oauth/token
    httpVerb: POST
    expectedStatus: 200
    body: |
      {
        ""grant_type"": ""client_credentials"",
        ""client_id"": ""{{input.clientId}}"",
        ""client_secret"": ""{{input.clientSecret}}""
      }
    output:
      dto: ""{{response.body}}""
      http_status: ""{{response.status}}""

endStage:
  output:
    accessToken: ""{{stage:json(get_token.output.dto).access_token}}""
    expiresIn: ""{{stage:json(get_token.output.dto).expires_in}}""
";

        return new WorkflowDesign
        {
            Name = "authentication",
            Path = "workflows/authentication.yaml",
            Yaml = yaml,
            Stages = 1,
            Description = "OAuth authentication workflow"
        };
    }

    /// <summary>
    /// Generates dependencies between workflows
    /// </summary>
    private static List<WorkflowDependency> GenerateWorkflowDependencies(List<WorkflowDesign> workflows)
    {
        var dependencies = new List<WorkflowDependency>();

        for (int i = 1; i < workflows.Count; i++)
        {
            dependencies.Add(new WorkflowDependency
            {
                From = workflows[i - 1].Name,
                To = workflows[i].Name,
                Reason = "Sequential execution required"
            });
        }

        return dependencies;
    }

    /// <summary>
    /// Generates test scenarios for the system
    /// </summary>
    public List<TestScenario> GenerateTestScenarios(
        List<WorkflowDesign> workflows,
        SystemRequirements requirements)
    {
        var scenarios = new List<TestScenario>();

        // Happy path test
        scenarios.Add(new TestScenario
        {
            Name = "happy_path",
            Description = "All stages succeed with valid data",
            ExpectedBehavior = "All workflows complete successfully",
            MockFile = "mocks/happy_path.yaml"
        });

        // Error handling tests
        if (requirements.IncludeErrorHandling)
        {
            scenarios.Add(new TestScenario
            {
                Name = "first_stage_failure",
                Description = "First stage returns error",
                ExpectedBehavior = "Workflow stops and returns error",
                MockFile = "mocks/first_stage_error.yaml"
            });

            scenarios.Add(new TestScenario
            {
                Name = "network_timeout",
                Description = "API call times out",
                ExpectedBehavior = "Retry policy activates and eventually succeeds",
                MockFile = "mocks/timeout.yaml"
            });
        }

        return scenarios;
    }

    /// <summary>
    /// Estimates total execution time
    /// </summary>
    private static string EstimateExecutionTime(List<WorkflowDesign> workflows)
    {
        var totalStages = workflows.Sum(w => w.Stages);
        var estimatedSeconds = totalStages * 2; // Assume 2 seconds per stage

        return estimatedSeconds switch
        {
            < 10 => $"~{estimatedSeconds}s (fast)",
            < 30 => $"~{estimatedSeconds}s (moderate)",
            _ => $"~{estimatedSeconds}s (slow)"
        };
    }
}
