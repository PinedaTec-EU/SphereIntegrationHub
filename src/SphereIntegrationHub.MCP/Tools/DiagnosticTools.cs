using SphereIntegrationHub.MCP.Core;
using SphereIntegrationHub.MCP.Services;
using SphereIntegrationHub.MCP.Services.Integration;

namespace SphereIntegrationHub.MCP.Tools;

/// <summary>
/// Explains a validation error with suggestions
/// </summary>
[McpTool("explain_validation_error", "Explains a validation error and provides suggestions for fixing it", Category = "Diagnostic", Level = "L1")]
public sealed class ExplainValidationErrorTool : IMcpTool
{
    public ExplainValidationErrorTool(SihServicesAdapter adapter)
    {
        // No specific dependencies needed for this tool
    }

    public string Name => "explain_validation_error";
    public string Description => "Explains a validation error and provides detailed suggestions for fixing it";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            errorCategory = new
            {
                type = "string",
                description = "Error category (e.g., 'Stage', 'Workflow', 'Parse')"
            },
            errorMessage = new
            {
                type = "string",
                description = "The error message"
            },
            context = new
            {
                type = "object",
                description = "Optional additional context (stage name, field, etc.)"
            }
        },
        required = new[] { "errorCategory", "errorMessage" }
    };

    public Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var errorCategory = arguments?.GetValueOrDefault("errorCategory")?.ToString()
            ?? throw new ArgumentException("errorCategory is required");
        var errorMessage = arguments?.GetValueOrDefault("errorMessage")?.ToString()
            ?? throw new ArgumentException("errorMessage is required");

        var explanation = GenerateExplanation(errorCategory, errorMessage);
        return Task.FromResult<object>(explanation);
    }

    private static object GenerateExplanation(string category, string message)
    {
        var explanation = new
        {
            category,
            message,
            explanation = GetDetailedExplanation(category, message),
            suggestions = GetSuggestions(category, message),
            examples = GetExamples(category, message),
            relatedDocs = GetRelatedDocs(category)
        };

        return explanation;
    }

    private static string GetDetailedExplanation(string category, string message)
    {
        return category.ToLowerInvariant() switch
        {
            "stage" when message.Contains("name is required") =>
                "Every stage in a workflow must have a unique 'name' field. The stage name is used to reference the stage's outputs in subsequent stages and for debugging purposes.",

            "stage" when message.Contains("type is required") =>
                "Every stage must specify a 'type' field that determines what kind of operation the stage performs. Valid types include: api, transform, condition, loop, workflow-ref, and parallel.",

            "stage" when message.Contains("API stage requires") =>
                "API stages are used to make HTTP requests to external APIs. They require 'api' (the API name), 'endpoint' (the path), and typically 'verb' (GET, POST, PUT, DELETE) fields.",

            "workflow" when message.Contains("name is required") =>
                "Every workflow must have a 'name' field that uniquely identifies the workflow. This name is used when referencing the workflow from other workflows.",

            "workflow" when message.Contains("version is required") =>
                "Every workflow must have a 'version' field (e.g., '1.0', '2.1') to track changes and ensure compatibility.",

            "parse" =>
                "The YAML file could not be parsed. This usually means there's a syntax error in the YAML structure, such as incorrect indentation, missing colons, or invalid characters.",

            _ => $"A validation error occurred in the {category} category: {message}"
        };
    }

    private static List<string> GetSuggestions(string category, string message)
    {
        var suggestions = new List<string>();

        if (message.Contains("name is required"))
        {
            suggestions.Add("Add a 'name' field with a descriptive, unique identifier");
            suggestions.Add("Use lowercase with underscores (e.g., 'fetch_user_data')");
            suggestions.Add("Avoid spaces and special characters in names");
        }
        else if (message.Contains("type is required"))
        {
            suggestions.Add("Add a 'type' field specifying the stage type");
            suggestions.Add("Valid types: api, transform, condition, loop, workflow-ref, parallel");
            suggestions.Add("Most common type is 'api' for HTTP requests");
        }
        else if (message.Contains("API stage requires"))
        {
            suggestions.Add("Ensure the stage has 'api', 'endpoint', and 'verb' fields");
            suggestions.Add("The 'api' should match a name from your API catalog");
            suggestions.Add("The 'endpoint' should be a valid path (e.g., '/api/users/{id}')");
            suggestions.Add("The 'verb' should be GET, POST, PUT, DELETE, or PATCH");
        }
        else if (message.Contains("version is required"))
        {
            suggestions.Add("Add a 'version' field at the top level of the workflow");
            suggestions.Add("Use semantic versioning (e.g., '1.0', '1.1', '2.0')");
        }
        else if (category.ToLowerInvariant() == "parse")
        {
            suggestions.Add("Check YAML indentation (use 2 spaces, not tabs)");
            suggestions.Add("Ensure all colons are followed by a space");
            suggestions.Add("Verify quotes are properly matched");
            suggestions.Add("Use a YAML validator to identify syntax errors");
        }

        return suggestions;
    }

    private static List<object> GetExamples(string category, string message)
    {
        var examples = new List<object>();

        if (message.Contains("name is required"))
        {
            examples.Add(new
            {
                description = "Correct stage with name",
                code = "- name: fetch_user\n  type: api\n  api: accounts\n  endpoint: /api/users/{id}"
            });
        }
        else if (message.Contains("API stage requires"))
        {
            examples.Add(new
            {
                description = "Complete API stage",
                code = "- name: create_account\n  type: api\n  api: accounts\n  endpoint: /api/accounts\n  verb: POST\n  body:\n    username: \"{{ input.username }}\"\n    email: \"{{ input.email }}\""
            });
        }

        return examples;
    }

    private static List<string> GetRelatedDocs(string category)
    {
        return category.ToLowerInvariant() switch
        {
            "stage" => ["Workflow Stage Reference", "API Stage Configuration", "Stage Types Guide"],
            "workflow" => ["Workflow Structure", "Workflow Metadata", "Best Practices"],
            "parse" => ["YAML Syntax Guide", "Common YAML Errors", "Validation Rules"],
            _ => ["General Documentation", "Troubleshooting Guide"]
        };
    }
}

/// <summary>
/// Gets plugin capabilities information
/// </summary>
[McpTool("get_plugin_capabilities", "Gets information about available plugin capabilities", Category = "Diagnostic", Level = "L1")]
public sealed class GetPluginCapabilitiesTool : IMcpTool
{
    public GetPluginCapabilitiesTool(SihServicesAdapter adapter)
    {
        // No specific dependencies needed
    }

    public string Name => "get_plugin_capabilities";
    public string Description => "Gets information about available plugin capabilities and stage types";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            pluginType = new
            {
                type = "string",
                description = "Optional plugin type to get specific capabilities for"
            }
        },
        required = Array.Empty<string>()
    };

    public Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var pluginType = arguments?.GetValueOrDefault("pluginType")?.ToString();
        return Task.FromResult(PluginCapabilitiesRegistry.GetCapabilities(pluginType));
    }
}

/// <summary>
/// Suggests resilience configuration for a stage
/// </summary>
[McpTool("suggest_resilience_config", "Suggests resilience configuration (retry, timeout, circuit breaker) for a stage", Category = "Diagnostic", Level = "L1")]
public sealed class SuggestResilienceConfigTool : IMcpTool
{
    public SuggestResilienceConfigTool(SihServicesAdapter adapter)
    {
        // No specific dependencies needed
    }

    public string Name => "suggest_resilience_config";
    public string Description => "Suggests resilience configuration based on stage type and operation characteristics";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            stageType = new
            {
                type = "string",
                description = "Stage type (api, transform, etc.)"
            },
            operation = new
            {
                type = "string",
                description = "Operation description or endpoint"
            },
            critical = new
            {
                type = "boolean",
                description = "Whether this is a critical operation"
            }
        },
        required = new[] { "stageType" }
    };

    public Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var stageType = arguments?.GetValueOrDefault("stageType")?.ToString()
            ?? throw new ArgumentException("stageType is required");
        var operation = arguments?.GetValueOrDefault("operation")?.ToString() ?? "";
        var critical = arguments?.GetValueOrDefault("critical")?.ToString()?.ToLowerInvariant() == "true";

        var suggestion = GenerateResilienceSuggestion(stageType, operation, critical);
        return Task.FromResult<object>(suggestion);
    }

    private static object GenerateResilienceSuggestion(string stageType, string operation, bool critical)
    {
        var isReadOperation = operation.Contains("GET", StringComparison.OrdinalIgnoreCase) ||
                              operation.Contains("fetch", StringComparison.OrdinalIgnoreCase) ||
                              operation.Contains("read", StringComparison.OrdinalIgnoreCase);

        var isWriteOperation = operation.Contains("POST", StringComparison.OrdinalIgnoreCase) ||
                               operation.Contains("PUT", StringComparison.OrdinalIgnoreCase) ||
                               operation.Contains("DELETE", StringComparison.OrdinalIgnoreCase) ||
                               operation.Contains("create", StringComparison.OrdinalIgnoreCase) ||
                               operation.Contains("update", StringComparison.OrdinalIgnoreCase);

        return new
        {
            stageType,
            operation,
            critical,
            recommendations = new
            {
                retry = new
                {
                    enabled = stageType.ToLowerInvariant() == "api" && isReadOperation,
                    maxRetries = critical ? 5 : 3,
                    backoffType = "exponential",
                    initialDelay = "1s",
                    maxDelay = "30s",
                    reason = isReadOperation
                        ? "Read operations are safe to retry"
                        : "Write operations should be retried cautiously to avoid duplicates"
                },
                timeout = new
                {
                    enabled = stageType.ToLowerInvariant() == "api",
                    timeout = critical ? "60s" : "30s",
                    reason = "Prevents hanging on slow or unresponsive services"
                },
                circuitBreaker = new
                {
                    enabled = critical && stageType.ToLowerInvariant() == "api",
                    failureThreshold = 5,
                    durationOfBreak = "30s",
                    reason = critical
                        ? "Protects critical services from cascading failures"
                        : "Not recommended for non-critical operations"
                },
                fallback = new
                {
                    enabled = critical,
                    strategy = isReadOperation ? "cache" : "queue",
                    reason = critical
                        ? "Critical operations should have fallback strategies"
                        : "Optional for non-critical operations"
                }
            },
            example = GenerateResilienceExample(stageType, isReadOperation, critical)
        };
    }

    private static object GenerateResilienceExample(string stageType, bool isReadOperation, bool critical)
    {
        if (stageType.ToLowerInvariant() != "api")
        {
            return new
            {
                message = "Resilience policies are primarily for API stages"
            };
        }

        return new
        {
            yaml = @$"resilience:
  - name: standard_api_policy
    retry:
      maxRetries: {(critical ? 5 : 3)}
      backoff: exponential
      initialDelay: 1s
      maxDelay: 30s
    timeout: {(critical ? "60s" : "30s")}
    {(critical ? "circuitBreaker:\n      failureThreshold: 5\n      durationOfBreak: 30s" : "")}

stages:
  - name: example_stage
    type: api
    resilience-ref: standard_api_policy
    # ... rest of stage config"
        };
    }
}
