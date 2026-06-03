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

Subcommands:

- `report <path-to-json-or-dir>`: generates an interactive HTML report from one report JSON file or a directory of reports. It also loads `*.workflow.snapshot.json` files from the same directory, a sibling `snapshots/` directory, an explicit `--snapshot` path, or the repo baseline declared as `api.catalog` `baselineSnapshot`.
- `snapshot create <path-to-report-json>`: creates a stable regression snapshot from a known-good execution report.
- `snapshot compare <path-to-report-json> --snapshot <snapshot-json>`: compares a later execution report against a stored snapshot.

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

Create a regression snapshot:

```bash
sih snapshot create \
  ./output/create-account.01J....workflow.report.json \
  --name happy-path
```

By default, snapshots are written to `./snapshots/{workflow-name}.{snapshot-name}.workflow.snapshot.json`, next to the report output folder.

Compare a later execution against the snapshot:

```bash
sih snapshot compare \
  ./output/create-account.01K....workflow.report.json \
  --snapshot ./snapshots/create-account.happy-path.workflow.snapshot.json
```

`snapshot compare` exits with `0` when the canonical execution baseline matches and `1` when meaningful differences are found.

Open reports with baseline snapshots available in the viewer:

```bash
sih report ./output \
  --catalog ./api.catalog \
  --snapshot ./snapshots \
  --no-open
```

`api.catalog` can define the repository default baseline snapshot:

```yaml
- version: "1.0"
  baselineSnapshot: ./snapshots/create-account.happy-path.workflow.snapshot.json
  definitions: []
```

Because `api.catalog` is currently a list of catalog versions, SIH first looks for `baselineSnapshot` on the report workflow version; if none exists, it uses the first `baselineSnapshot` in the catalog as the repo default.

When snapshots are present, the report viewer shows report/baseline labels in the context row and exposes report/baseline selection from the `Context` modal. `Compare` is enabled by default. The timeline keeps the active execution as a solid bar and shows the selected baseline as a vertical timing marker over the same rail. The detail panel shows per-stage differences, a timeline comparison widget, and a compact baseline/current execution grid. The UI also has a `Load baseline JSON` action to load a snapshot from another local path.

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
- assertion diagnostics and assertion metrics when workflows define assertions
- baseline comparison data when a snapshot is selected in the viewer
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

- **Compact header**: application version, `Graph`, `Context`, `Compare`, and theme controls.
- **Context row**: selected report execution and selected baseline snapshot.
- **Meta bar**: start time, total duration, environment, workflow version, stage count, workflow count, depth, and result status.
- **Summary chips**: high-signal stage, failure, assertion, and baseline-diff state, with a right-aligned `More metrics` control for detailed counts.
- **Jaeger-style timeline**: each current stage is rendered as a solid horizontal bar positioned at its real start offset and sized proportionally to its duration. Bars are color-coded (green = ok, red = error, grey = skipped, purple = mocked) and include the HTTP method badge and workflow nesting indent.
- **Baseline markers**: when comparison is enabled, the selected baseline snapshot is rendered as a compact vertical timing marker over the current stage rail.
- **Workflow constellation graph**: `Graph` opens a modal with an auto-laid-out SVG of workflow/stage relationships; stage nodes jump back to the trace detail.
- **Stage detail panel**: clicking any bar shows the stage's full metadata — kind, status, HTTP method/URI/status code, request and response headers and body, ensure config, assertions, baseline differences, timeline comparison, jump target, and output values.
- **Context loading**: `Context` lets the user switch report executions and baseline snapshots. Report JSON and baseline snapshot JSON can also be loaded from another local path.
