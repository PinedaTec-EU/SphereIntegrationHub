namespace SphereIntegrationHub.MCP.Services;

internal static class PluginCapabilitiesRegistry
{
    public static object GetCapabilities(string? pluginType)
    {
        var stageTypes = new[]
        {
            new
            {
                type = "endpoint",
                description = "Calls an HTTP endpoint using the runtime workflow schema",
                requiredFields = new[] { "name", "kind", "apiRef", "endpoint", "httpVerb" },
                optionalFields = new[]
                {
                    "expectedStatus",
                    "expectedStatuses",
                    "headers",
                    "query",
                    "body",
                    "bodyFile",
                    "dataFile",
                    "forEach",
                    "itemName",
                    "indexName",
                    "runIf",
                    "output",
                    "jumpOnStatus",
                    "onStatus",
                    "ensure",
                    "retry",
                    "circuitBreaker",
                    "message",
                    "debug",
                    "set",
                    "context",
                    "mock"
                },
                capabilities = new[]
                {
                    "HTTP requests",
                    "Status branching with expectedStatuses/onStatus/jumpOnStatus/ensure",
                    "Inline or file-based request bodies",
                    "Collection iteration with forEach/dataFile",
                    "Response capture into output"
                }
            },
            new
            {
                type = "workflow",
                description = "Executes another workflow using workflowRef",
                requiredFields = new[] { "name", "kind", "workflowRef" },
                optionalFields = new[]
                {
                    "inputs",
                    "forEach",
                    "dataFile",
                    "itemName",
                    "indexName",
                    "runIf",
                    "allowVersion",
                    "message",
                    "debug",
                    "set",
                    "context",
                    "mock"
                },
                capabilities = new[]
                {
                    "Workflow composition",
                    "Input/output mapping",
                    "Nested execution",
                    "Collection iteration over child workflows"
                }
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
                    description = "Dynamic value substitution using runtime tokens and JSON paths",
                    examples = new[]
                    {
                        "{{input.userId}}",
                        "{{context:item.id}}",
                        "{{stage:create-account.output.dto}}",
                        "{{response.body.id}}"
                    }
                },
                new
                {
                    feature = "Expressions",
                    description = "runIf supports comparisons, boolean operators, and JSON helper functions",
                    examples = new[]
                    {
                        "exists({{context:item}})",
                        "jsonLength({{input.items}}) > 0",
                        "!isEmptyJson({{response.body}})",
                        "{{stage:create.output.http_status}} in [200, 201, 409]"
                    }
                },
                new
                {
                    feature = "Idempotent Branching",
                    description = "Endpoint stages can continue on non-happy statuses without failing first",
                    examples = new[]
                    {
                        "expectedStatuses: [200, 201, 409]",
                        "onStatus.409.jumpTo = load-existing",
                        "ensure.mode = CreateIfMissing",
                        "jumpOnStatus.404 = create-resource"
                    }
                },
                new
                {
                    feature = "External Data Files",
                    description = "Large payloads and collections can be loaded from JSON/YAML files",
                    examples = new[]
                    {
                        "bodyFile: ./payloads/create-account.json",
                        "dataFile: ./seed/accounts.json",
                        "forEach: \"{{input.items}}\""
                    }
                },
                new
                {
                    feature = "Complex Inputs",
                    description = "Workflow inputs may be scalar, object, or array values",
                    examples = new[]
                    {
                        "type: Object",
                        "type: Array",
                        "{{input.payload.customer.id}}"
                    }
                },
                new
                {
                    feature = "Context Management",
                    description = "Shared state across stages",
                    examples = new[] { "{{context.tokenId}}", "{{context:item}}", "{{global.accountId}}" }
                },
                new
                {
                    feature = "Authoring Guidance",
                    description = "Prefer real runtime fields and avoid invented plugin stage types",
                    examples = new[]
                    {
                        "Use kind: Endpoint or kind: Workflow",
                        "Prefer ensure for create-if-missing bootstrap stages",
                        "Prefer expectedStatuses for idempotent create/bootstrap flows",
                        "Prefer bodyFile/dataFile when payloads are large"
                    }
                }
            }
        };
    }
}
