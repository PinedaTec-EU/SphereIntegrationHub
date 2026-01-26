# Plugins

SphereIntegrationHub uses stage plugins to validate and execute workflow stages. The core always includes the built-in `workflow` plugin and loads the rest from `workflows.config`.

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
```

Rules:

- The `workflow` plugin is built-in and always loaded.
- At least one additional plugin must be configured (for example `http`).
- Plugins are loaded from a `plugins` folder next to `workflows.config`.
  - Built-in plugins (like `http`) do not require a DLL file.
  - External plugins must be available as `plugins/<pluginId>.dll`.

## Plugin contract

Plugins must implement `IStagePlugin` and provide a parameterless constructor. Validation is defined separately via `IStageValidator`.

```csharp
public interface IStagePlugin
{
    string Id { get; }
    IReadOnlyCollection<string> StageKinds { get; }
    StagePluginCapabilities Capabilities { get; }

    Task<string?> ExecuteAsync(
        WorkflowStageDefinition stage,
        StageExecutionContext context,
        CancellationToken cancellationToken);
}

public interface IStageValidator
{
    string Id { get; }
    IReadOnlyCollection<string> StageKinds { get; }

    void ValidateStage(
        WorkflowStageDefinition stage,
        StageValidationContext context,
        List<string> errors);
}
```

Notes:

- `Id` is the plugin name used in `workflows.config`.
- `StageKinds` are the stage `kind` values that this plugin owns.
- `Capabilities` control output type, mock handling, and `jumpOnStatus` support.
- `ValidateStage` should add errors instead of throwing for user errors.
- `ExecuteAsync` can return a jump target (or `null`).
- A plugin should expose a matching `IStageValidator` (it may be the same class implementing both).

## Rules and constraints

- Each `StageKind` must be unique across all loaded plugins.
- A plugin cannot reuse a `kind` already registered by another plugin.
- Each `StageKind` must be unique across all loaded validators.
- The system aborts if only the built-in `workflow` plugin is loaded.
- Plugin ids must be unique (case-insensitive).

## Stage kinds

The plugin `StageKind` decides how a stage is validated and executed. The built-in plugins expose:

- `workflow` (built-in)
- `http` (built-in)
- `endpoint` (alias for the `http` plugin)

Example:

```yaml
stages:
  - name: "login"
    kind: "http"
    apiRef: "example-service"
    endpoint: "/api/auth/login"
    httpVerb: "POST"
    expectedStatus: 200

  - name: "authenticate"
    kind: "workflow"
    workflowRef: "login-workflow"
```
