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
                    "forEachSequential",
                    "itemName",
                    "indexName",
                    "runIf",
                    "output",
                    "secretOutputs",
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
                    "Parallel forEach by default with optional sequential override",
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
                    "forEachSequential",
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
                    "Collection iteration over child workflows",
                    "Parallel forEach by default with optional sequential override"
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
                        "{{response.body.id}}",
                        "{{stage:create-account.workflow.output.accountAppId}}",
                        "{{stage:create-account.workflow.result.status}}"
                    }
                },
                new
                {
                    feature = "Expressions",
                    description = "runIf supports comparisons, compound boolean operators, safe missing-token checks, and JSON/control helper functions",
                    examples = new[]
                    {
                        "exists({{context:item}})",
                        "empty({{stage:create.output.accountId}})",
                        "coalesce({{stage:create.output.accountId}}, {{context:accountId}}, 'pending')",
                        "jsonLength({{input.items}}) > 0",
                        "!isEmptyJson({{response.body}})",
                        "{{stage:create.output.http_status}} in [200, 201, 409]",
                        "{{stage:create-a?.output.id}} != null || {{stage:create-b?.output.id}} != null"
                    }
                },
                new
                {
                    feature = "Skipped-Stage Output Resolution",
                    description = "When a stage is skipped due to runIf, references to its outputs resolve to empty string rather than failing. Three mechanisms are available for fine-grained control.",
                    examples = new[]
                    {
                        "Mejora 1 — automatic: {{stage:skipped-stage.output.id}} resolves to '' in template strings",
                        "Mejora 2 — coalesce in templates: {{coalesce(stage:branch-a.output.id, stage:branch-b.output.id)}}",
                        "Mejora 3 — safe stage nav: {{stage:maybe-skipped?.output.id}} returns '' when absent",
                        "Mejora 3 — safe key nav: {{stage:ran-stage.output.optionalKey?}} returns '' when key absent",
                        "Combined: {{coalesce(stage:create-a?.output.id, stage:create-b?.output.id)}}"
                    }
                },
                new
                {
                    feature = "Optional Paths",
                    description = "JSON token paths may use ? suffixes so missing nested segments resolve safely. Stage tokens also support ? on stage name and output key.",
                    examples = new[]
                    {
                        "{{response.body.account.status?}}",
                        "{{stage:create.output.dto.items.0.id?}}",
                        "{{input.payload.customer.id?}}",
                        "{{stage:create?.output.id}} — safe if stage was skipped",
                        "{{stage:create.output.appId?}} — safe if key is absent in output"
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
                        "forEach: \"{{input.items}}\"",
                        "forEachSequential: true"
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
                        "Prefer bodyFile/dataFile when payloads are large",
                        "Use forEachSequential only when ordering or shared-state constraints require sequential execution",
                        "Use sample workflows under samples/ as runtime-aligned references"
                    }
                },
                new
                {
                    feature = "Execution Reporting",
                    description = "Runtime executions can emit JSON/HTML reports with stage-level diagnostics and configurable HTTP capture",
                    examples = new[]
                    {
                        "--report-format both",
                        "--capture-http headers",
                        "--capture-http bodies",
                        "reporting.summaryConsole = true",
                        "stage.ForEachExecutionMode = Parallel|Sequential",
                        "{name}.{executionId}.workflow.output",
                        "{name}.{executionId}.workflow.report.json"
                    }
                },
                new
                {
                    feature = "Secret Masking",
                    description = "Sensitive values are masked as ***** in the execution report. Mark inputs, init-stage variables, or specific output keys as secret. The key name is always visible.",
                    examples = new[]
                    {
                        "input: secret: true  →  value masked in report.Inputs",
                        "initStage.variables: secret: true  →  value masked wherever it appears in outputs",
                        "stage: secretOutputs: [accessToken]  →  value masked in stage output record",
                        "endStage: secretOutputs: [accessToken]  →  value masked in report.Output"
                    }
                }
            }
        };
    }
}
