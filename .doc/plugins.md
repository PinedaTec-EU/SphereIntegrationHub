# Plugins

SphereIntegrationHub uses stage plugins to validate and execute workflow stages. `workflow` remains built in. Transport/protocol stages are resolved through plugins declared in `workflows.config` and linked from `api.catalog`.

## Configuration

Create a `workflows.config` file next to your workflow files:

```yaml
features:
  openTelemetry: true
openTelemetry:
  serviceName: "SphereIntegrationHub.cli"
  endpoint: "http://localhost:4317"
  consoleExporter: false
  debugConsole: false
plugins:
  - http
  - openai
  - amqp
```

Rules:

- The `workflow` stage kind is built into the runtime.
- If `plugins` is omitted, the built-in `http` plugin is enabled by default for compatibility and the runtime emits a warning.
- The warning is transitional; a future release will require the `plugins` section explicitly.
- If `plugins` is present, only the listed plugins are enabled.
- External plugins are loaded from a `plugins` folder next to `workflows.config`.
- Each explicit plugin should also be declared in the selected `api.catalog` version under `plugins`.

## Plugin contract

Plugins must implement `IStagePlugin` and provide a parameterless constructor.

```csharp
public interface IStagePlugin
{
    StagePluginDescriptor Descriptor { get; }
    void ValidateStage(
        WorkflowStageDefinition stage,
        StagePluginValidationContext context,
        List<string> errors,
        List<string> warnings);

    Task<StagePluginExecutionResult> ExecuteAsync(
        WorkflowStageDefinition stage,
        StagePluginExecutionContext context,
        CancellationToken cancellationToken);
}
```

Notes:

- `Descriptor.Id` is the plugin name used in `workflows.config` and `api.catalog`.
- `Descriptor.StageKinds` are the stage `kind` values that this plugin owns.
- `Descriptor.ContractVersion` is the versioned contract checked during plugin pre-check.
- Plugin-specific stage properties should live under `stage.config`.
- `ValidateStage` should add errors instead of throwing for user/schema errors.
- `ExecuteAsync` returns a normalized response envelope consumed by the runtime.

## Rules and constraints

- Each `StageKind` must be unique across all loaded plugins.
- A plugin cannot reuse a `kind` already registered by another plugin.
- Each `StageKind` must be unique across all loaded validators.
- The system aborts if only the built-in `workflow` plugin is loaded.
- Plugin ids must be unique (case-insensitive).

## Stage kinds

The plugin `StageKind` decides how a stage is validated and executed. The built-in plugins expose:

- `Workflow` (built-in stage kind)
- `Http` / `Endpoint` (handled by the `http` plugin)
- `LLM` / `OpenAI` (handled by the `openai` plugin)

Example:

```yaml
stages:
  - name: "login"
    kind: "Http"
    expectedStatus: 200
    config:
      apiRef: "example-service"
      endpoint: "/api/auth/login"
      httpVerb: "POST"

  - name: "authenticate"
    kind: "Workflow"
    workflowRef: "login-workflow"
```

## OpenAI LLM plugin

The built-in `openai` plugin calls an OpenAI-compatible Responses API endpoint. It is useful when a workflow needs an LLM/SLM step to transform data before another API request.

Declare the plugin and connection in `api.catalog`:

```yaml
- version: 3.11
  plugins:
    - id: openai
      contractVersion: "1.0"
      runtimeVersion: "1.0"
  definitions: []
  connections:
    - name: openai-main
      type: llm
      provider: openai
      baseUrl:
        local: https://api.openai.com/v1
      apiKeySecret: "{{input.openaiApiKey}}"
```

Use `kind: LLM` in the workflow:

```yaml
stages:
  - name: "prepare-payload"
    kind: "LLM"
    expectedStatus: 200
    config:
      connectionRef: "openai-main"
      model: "gpt-5.4-mini"
      prompts:
        system:
          text: "You transform workflow data into API-ready JSON."
        input:
          file: "./prompts/create-customer.md"
        output:
          text: "Return only JSON matching the configured schema."
      reasoning:
        effort: "low"
      generation:
        temperature: 0.2
        responseFormat: "schema"
      output:
        schemaName: "customer_payload"
        schemaStrict: true
        schema:
          type: object
          required: [name, country]
          properties:
            name:
              type: string
            country:
              type: string
      limits:
        maxInputTokens: 8000
        maxOutputTokens: 1200
        maxTotalTokens: 9200
        timeoutSeconds: 60
```

Default plugin outputs include `text`, `inputTokens`, `outputTokens`, `totalTokens`, `cachedInputTokens`, `reasoningTokens`, `finishReason`, `durationMs`, `requestId`, `model`, and `provider`. These can be referenced as normal stage outputs, for example `{{stage:prepare-payload.output.totalTokens}}`.

See [`samples/openai-llm/sample-openai-llm.workflow`](../samples/openai-llm/sample-openai-llm.workflow) for a runnable mocked example.
