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
