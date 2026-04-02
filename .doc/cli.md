# CLI Usage

Basic options:

- `--workflow <path>`: workflow file to execute.
- `--env <environment>`: environment key for base URL (dev/pre/prod/etc.).
- `--catalog <path>`: optional catalog path.
- `--envfile <path>`: optional `.env` override for the root workflow.
- `--mocked`: use mock payloads/outputs when defined in stages.
- `--varsfile <path>`: optional workflow vars file (must be `.wfvars`).
- `--dry-run`: validate and print the execution plan (no HTTP calls).
- `--verbose`: detailed output for dry-run and cache operations.
- `--debug`: print stage debug sections before invocation.
- `--refresh-cache`: force re-download of swagger definitions.
- `--report-format <json|html|both|none>`: controls post-execution report generation.
- `--capture-http <none|headers|bodies>`: controls how much HTTP data is captured in reports.
- `--no-redact`: disables header/body redaction in reports.
- `--no-summary`: disables the final console execution summary.

Examples:

Dry-run:

```bash
SphereIntegrationHub.cli \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --dry-run \
  --verbose
```

Execute:

```bash
SphereIntegrationHub.cli \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --varsfile ./src/resources/workflows/create-account.wfvars
```

Override root `.env`:

```bash
SphereIntegrationHub.cli \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --envfile ./workflows/create-account.env
```

Use mocks:

```bash
SphereIntegrationHub.cli \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --mocked
```

Generate JSON + HTML execution reports with body capture:

```bash
SphereIntegrationHub.cli \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --report-format both \
  --capture-http bodies
```

Vars file auto-detection:

- If `--varsfile` is not provided and a file named `{workflow}.wfvars` exists alongside the workflow, it is used automatically.
- `.wfvars` can scope values by environment and version (see `variables.md`).
- `--verbose` prints the resolved source for each variable (global/environment/version).

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

## Generated artifacts

When reporting is enabled, SIH writes one or both of:

- `{workflow-name}.{executionId}.workflow.report.json`
- `{workflow-name}.{executionId}.workflow.report.html`

When workflow output persistence is enabled, SIH also writes:

- `{workflow-name}.{executionId}.workflow.output`

The report contains:

- execution metadata and result
- stage timeline with durations
- skipped, jumped, mocked, and failed stage states
- retry counts and ensure status
- HTTP request/response summary according to `captureHttp`
- output values as resolved at the end of the run
