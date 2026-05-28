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
- `--assertion-failures-block <true|false>`: controls whether assertion failures fail the workflow for this execution. Overrides `api.catalog`.
- `--no-redact`: disables header/body redaction in reports.
- `--no-summary`: disables the final console execution summary.

Examples:

Dry-run:

```bash
sih \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --dry-run \
  --verbose
```

Execute:

```bash
sih \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --varsfile ./src/resources/workflows/create-account.wfvars
```

Override root `.env`:

```bash
sih \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --envfile ./workflows/create-account.env
```

Use mocks:

```bash
sih \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --mocked
```

Generate JSON + HTML execution reports with body capture:

```bash
sih \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --report-format both \
  --capture-http bodies
```

Run with non-blocking assertion failures:

```bash
sih \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --assertion-failures-block false
```

Assertion failure blocking defaults to `true`. Runtime precedence is:

1. `assertions[].blocking`
2. `--assertion-failures-block <true|false>`
3. selected `api.catalog` version `assertionFailuresBlock`
4. default `true`

When disabled, failed assertions are warnings: execution continues, the console prints a warning, and the report marks the assertion as failed/non-blocking.

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

## Interactive trace report (`sih report`)

`sih report` is a standalone command that reads a `.workflow.report.json` artifact and generates a self-contained interactive HTML trace report, then opens it in the browser automatically.

```bash
sih report <path-to-json> [--output <dir>] [--no-open]
```

Options:

- `<path>`: path to the `.workflow.report.json` artifact (positional).
- `-x, --execution <path>`: alternative flag for the JSON path.
- `-o, --output <dir>`: output directory for the HTML file (defaults to same directory as the JSON).
- `--no-open`: generate the HTML but do not open the browser.

Examples:

Generate and open immediately:

```bash
sih report ./output/create-account.01J....workflow.report.json
```

Generate into a different directory, no browser:

```bash
sih report ./output/create-account.01J....workflow.report.json \
  --output ./reports \
  --no-open
```

The generated `*.workflow.report.html` is fully self-contained (no CDN dependencies) and includes:

- **Header**: workflow name, execution ID, result status, and environment.
- **Meta bar**: start time, total duration, version, stage count, and nesting depth.
- **Metrics chips**: total, executed, failed, skipped, mocked, and retry counts.
- **Jaeger-style timeline**: each stage is rendered as a horizontal bar positioned at its real start offset and sized proportionally to its duration. Bars are color-coded (green = ok, red = error, grey = skipped, purple = mocked) and include the HTTP method badge and workflow nesting indent.
- **Stage detail panel**: clicking any bar shows the stage's full metadata — kind, status, HTTP method/URI/status code, request and response headers and body, ensure config, jump target, and stage output values.
- **Load another execution**: the HTML includes a file picker to load any other `.workflow.report.json` and re-render the trace without generating a new file.
