# Execution Reporting

SphereIntegrationHub can persist each workflow run as local execution artifacts for post-run diagnostics, auditability, and sharing.

## What you get

- JSON report for machine-readable inspection and automation.
- Self-contained HTML report for human investigation.
- Console summary with execution id and generated artifact paths.
- Configurable HTTP capture: `none`, `headers`, or `bodies`.
- Redaction by default for sensitive headers and JSON fields.

## Generated artifacts

When reporting is enabled, SIH writes one or both of:

- `{workflow-name}.{executionId}.workflow.report.json`
- `{workflow-name}.{executionId}.workflow.report.html`

When workflow output persistence is enabled, SIH also writes:

- `{workflow-name}.{executionId}.workflow.output`

## Interactive HTML viewer

The generated HTML report is fully self-contained and focused on execution analysis.

It includes:

- Timeline overview of nested workflows and stage durations.
- Status and metrics summary for executed, skipped, failed, mocked, and retried stages.
- Stage detail panel with workflow result, resolved inputs, execution timing, and HTTP metadata.
- Execution switcher when multiple report artifacts exist in the same output folder.

### Timeline overview

![Execution report timeline overview](./Screenshots/execution-report-timeline-overview.png)

### Execution switcher

![Execution report run selector dropdown](./Screenshots/execution-report-run-selector-dropdown.png)

### Stage details

![Execution report stage details](./Screenshots/execution-report-stage-details.png)

## CLI usage

Generate JSON + HTML reports during execution:

```bash
SphereIntegrationHub.cli \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --report-format both \
  --capture-http bodies
```

Generate an interactive HTML report from an existing JSON artifact:

```bash
sih report ./output/create-account.01J....workflow.report.json
```

Generate without opening the browser:

```bash
sih report ./output/create-account.01J....workflow.report.json \
  --output ./reports \
  --no-open
```

## Reporting configuration

Place reporting defaults in `workflows.config` next to the workflow:

```yaml
reporting:
  enabled: true
  format: "json"
  captureHttp: "headers"
  redactSensitiveData: true
  summaryConsole: true
```

Rules:

- CLI flags override `workflows.config`.
- `format: "none"` or `--report-format none` disables report files.
- `captureHttp: "headers"` stores redacted headers and metadata without persisting bodies.
- `captureHttp: "bodies"` additionally stores request/response bodies, still redacted unless `--no-redact` is used.

## Relationship to OpenTelemetry

Execution reports and OpenTelemetry are complementary:

- Execution reports are local per-run artifacts for investigation, CI artifacts, and audit trails.
- OpenTelemetry is for centralized tracing, dashboards, correlation, and alerts.

See [`.doc/telemetry.md`](./telemetry.md) for the telemetry model and [`.doc/cli.md`](./cli.md) for the full CLI flag reference.
