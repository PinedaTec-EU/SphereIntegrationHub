namespace SphereIntegrationHub.MCP.Services;

internal static class PluginCapabilitiesRegistry
{
    public static object GetCapabilities(string? pluginType)
    {
        var stageTypes = new[]
        {
            new
            {
                type = "api",
                description = "Makes HTTP requests to external APIs",
                requiredFields = new[] { "api", "endpoint" },
                optionalFields = new[] { "verb", "queryParams", "pathParams", "body", "headers", "output" },
                capabilities = new[] { "HTTP requests", "Parameter templating", "Response capture", "Error handling" }
            },
            new
            {
                type = "transform",
                description = "Transforms data using mappings or scripts",
                requiredFields = new[] { "mapping" },
                optionalFields = new[] { "script", "output" },
                capabilities = new[] { "Field mapping", "Data transformation", "Scripting support" }
            },
            new
            {
                type = "condition",
                description = "Conditionally executes stages based on expressions",
                requiredFields = new[] { "condition", "then" },
                optionalFields = new[] { "else" },
                capabilities = new[] { "Boolean expressions", "Conditional branching", "Variable comparison" }
            },
            new
            {
                type = "loop",
                description = "Iterates over collections",
                requiredFields = new[] { "items", "do" },
                optionalFields = new[] { "max-iterations", "output" },
                capabilities = new[] { "Array iteration", "Nested stages", "Aggregation" }
            },
            new
            {
                type = "workflow-ref",
                description = "Executes another workflow",
                requiredFields = new[] { "workflow" },
                optionalFields = new[] { "inputs", "output" },
                capabilities = new[] { "Workflow composition", "Input/output mapping", "Nested execution" }
            },
            new
            {
                type = "parallel",
                description = "Executes multiple stages in parallel",
                requiredFields = new[] { "stages" },
                optionalFields = new[] { "output" },
                capabilities = new[] { "Concurrent execution", "Result aggregation", "Failure handling" }
            }
        };

        if (!string.IsNullOrEmpty(pluginType))
        {
            var specific = stageTypes.FirstOrDefault(s =>
                s.type.Equals(pluginType, StringComparison.OrdinalIgnoreCase));

            if (specific != null)
                return specific;
        }

        return new
        {
            stageTypes,
            features = new[]
            {
                new
                {
                    feature = "Template Tokens",
                    description = "Dynamic value substitution using {{ }} syntax",
                    examples = new[] { "{{ input.userId }}", "{{ stages.fetch_user.output.body.name }}", "{{ system.timestamp }}" }
                },
                new
                {
                    feature = "Resilience Policies",
                    description = "Retry, timeout, and circuit breaker patterns",
                    examples = new[] { "retry with exponential backoff", "timeout after 30s", "circuit breaker on failures" }
                },
                new
                {
                    feature = "Context Management",
                    description = "Shared state across stages",
                    examples = new[] { "context.sessionToken", "context.userData" }
                }
            }
        };
    }
}
