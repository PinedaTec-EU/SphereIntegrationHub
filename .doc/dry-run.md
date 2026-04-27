# Dry-Run Validation

The `--dry-run` mode validates workflows and prints an execution plan without executing workflow endpoints.

If a referenced API definition includes `healthCheck` in the catalog, SIH performs that HTTP precheck before contract caching. When the configured readiness policy is exhausted, dry-run fails immediately.

Dry-run reports failures by phase:

1. Workflow loading
2. Vars resolution
3. Workflow validation
4. Environment validation
5. API health checks
6. API contract cache/bootstrap
7. Endpoint validation
8. Execution plan rendering

When a failure comes from a function or template token, dry-run keeps the original token and the exact workflow location in the error output whenever possible.

## What is validated

### 1) Workflow structure

- `version`, `id`, `name` are required.
- `input` names are unique.
- `initStage.variables` names are unique.
- `output: true` requires `endStage.output`.

### 2) References

- `references.workflows` entries must exist and resolve to valid workflow files.
- `references.apis` entries must exist and match a definition name in the catalog version.
- Version mismatch between parent and referenced workflows fails unless `allowVersion` is set on the stage.
- Workflow stage inputs:
  - Required inputs of the referenced workflow must be provided.
  - Unknown inputs are rejected.

### 3) Template tokens

All template tokens are validated for existence:

- `{{input.*}}` must exist in `input`.
- `{{global.*}}` must exist in `initStage.variables`.
- `{{context.*}}` must be syntactically valid.
- `{{env:*}}` must exist in the workflow `.env` (if configured) or the process environment.
- `.env` values are resolved recursively when possible, so chained values such as `CHILD={{env:BASE}}/child` are validated during dry-run.
- Circular or undefined `.env` references fail validation before execution.
- `{{stage:stage.output.key}}` must refer to a stage output key.
- `{{response.*}}` is only allowed inside endpoint stage `output` mappings and endpoint stage `message` templates.
- `{{rand:*}}` helpers are validated for function name, argument count, and literal argument type where static analysis is possible.
- Invalid random helpers report the exact reason, for example unsupported character sets or malformed date/datetime/time literals.
- Token visibility rules are defined in `.doc/workflow-schema.md` (Token visibility by section).

Validated locations:

- `initStage.variables.value`
- `initStage.context`
- Stage headers/query/body
- Stage inputs (workflow stages)
- Stage output mappings
- Stage `set` and `context`
- `runIf` expressions
- `endStage.output` and `endStage.context`
- `mock` payloads and outputs

`.env` resolution for dry-run:

- Root workflow uses `references.environmentFile` unless `--envfile` is provided.
- Child workflow `.env` files are merged with the parent (parent values win).
- Child and root `.env` values can reference other `.env` values or inherited parent values with `{{env:NAME}}`.

### 4) Endpoint validation

Using the cached API contract for the workflow version:

- API references are resolved.
- The endpoint path is validated against contract paths.
  - If the endpoint includes templates (e.g. `{{input.id}}`), the path is normalized and matched against `{param}` paths in the cached contract.
- The HTTP verb must exist for the matched path.
- Required parameters:
  - `query`: must exist in stage `query`.
  - `header`: must exist in stage `headers`.
  - `path`: number of placeholders must be satisfied by the endpoint path.
  - `body`/`formData`: requires a non-empty `body`.

### 5) Jump targets

- `jumpOnStatus` targets must be valid stage names or `endStage`.
- `jumpOnStatus` is only allowed on endpoint stages.

### 6) Mocks

- Endpoint stage mocks must define a JSON payload (`payload` or `payloadFile`, not both).
- Workflow stage mocks must define `mock.output`.
- Mock status codes must be positive integers.

`.env` resolution for dry-run:

- Root workflow uses `references.environmentFile` unless `--envfile` is provided.
- Child workflow `.env` files are merged with the parent (parent values win).

## Execution plan output

In `--dry-run --verbose`, the CLI prints:

- Workflow summary (version, id, file)
- Stage list, including headers/query/body
- `runIf`, `forEach`, `itemName`, `indexName`, `bodyFile`, `dataFile`, and stage message templates
- Resolved workflow reference paths and effective `.env` values when `--verbose` is enabled
- Stage outputs, `set`, and `context`
- Init-stage and end-stage context
- Endpoint validation details

Error reporting behavior:

- Workflow validation errors are grouped and counted before dry-run exits.
- Endpoint validation errors are grouped and counted separately from workflow validation.
- Health-check failures include target URL, retry count, duration, and the final reason.
- API contract bootstrap failures report the preflight phase explicitly.

## Exit behavior

- Any validation error fails the dry-run and returns a non-zero exit code.
