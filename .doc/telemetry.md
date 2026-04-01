# OpenTelemetry

SphereIntegrationHub emits OpenTelemetry traces + metrics from the CLI and shared services. Telemetry is disabled by default and configured per workflow directory via `workflows.config`.

## Configuration

Place a `workflows.config` file in the same directory as the workflow YAML being executed.

```yaml
features:
  openTelemetry: true
openTelemetry:
  serviceName: "SphereIntegrationHub.cli"
  endpoint: "http://localhost:4317"
  consoleExporter: false
  debugConsole: false
```

Notes:
- If `workflows.config` is missing, OpenTelemetry remains disabled (`features.openTelemetry` defaults to false).
- `endpoint` uses OTLP over gRPC. Use a collector or backend that accepts OTLP on that URL.
- `consoleExporter` writes spans/metrics to stdout.
- `debugConsole` is automatically enabled when running with `--debug` and forces console output.

## Running with telemetry

```bash
SphereIntegrationHub.cli \
  --workflow ./workflows/create-account.workflow \
  --env pre
```

Enable console output quickly for local debugging:

```bash
SphereIntegrationHub.cli \
  --workflow ./workflows/create-account.workflow \
  --env pre \
  --debug
```

## Source names

Use these names when wiring exporters/collectors:
- ActivitySource: `SphereIntegrationHub`
- Meter: `SphereIntegrationHub`
- Default service name: `SphereIntegrationHub.cli` (overridable via `openTelemetry.serviceName`)

## Spans you will see

Primary activities include:
- `cli.run`
- `workflow.load`, `workflow.validate`, `workflow.execute`, `workflow.stage`, `workflow.output.write`, `workflow.plan`
- `catalog.load`, `swagger.cache`, `endpoint.validate`
- `http.request`
- `template.resolve`, `template.token.resolve`
- `runif.parse`
- `random.generate`
- `environment.load`, `vars.load`, `keyvalue.load`
- `workflow.config.load`
- `api.baseurl.resolve`
- `mock.payload.load`, `mock.payload.load.file`, `mock.payload.validate`

Useful tags:
- `workflow.*` (`workflow.name`, `workflow.id`, `workflow.version`, `workflow.path`)
- `catalog.*` (`catalog.path`, `catalog.version`)
- `stage.*` (`stage.name`, `stage.kind`, `stage.status`, `stage.jump_target`, `stage.retry_count`, `stage.ensure_mode`, `stage.ensure_status`)
- `http.*` (`http.method`, `http.url.base`, `http.url.path`, `http.status_code`, `http.expected_statuses`)
- `file.*` (`file.path`, `file.separator`, `file.allow_export`)
- `template.*` (`template.length`, `template.token.root`)
- `expression.length`, `random.type`, `environment`, `api.definition`

## Exploiting telemetry

Typical pipelines:
- Use an OpenTelemetry Collector to receive OTLP (`openTelemetry.endpoint`) and forward to your backend (Jaeger, Tempo, Elastic, etc.).
- Use the span names + tags above to build dashboards/alerts for slow workflows, failing endpoints, or invalid templates.

## Relationship to execution reports

OpenTelemetry and execution reports solve different problems:

- OpenTelemetry is for centralized tracing, alerting, and cross-system correlation.
- Execution reports are local per-run artifacts for post-mortem inspection and auditability.

Recommended approach:

- Enable OpenTelemetry when you need fleet-level visibility or external dashboards.
- Keep reporting enabled for local diagnostics, CI job artifacts, and agent-friendly run inspection.
