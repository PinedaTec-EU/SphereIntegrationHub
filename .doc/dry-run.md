# Dry-Run Validation

The `--dry-run` mode validates workflows and prints an execution plan without calling any endpoints.

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
- `{{stage:stage.output.key}}` must refer to a stage output key.
- `{{response.*}}` is only allowed inside endpoint stage `output` mappings and endpoint stage `message` templates.
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

### 4) Endpoint validation

Using the cached swagger for the workflow version:

- API references are resolved.
- The endpoint path is validated against swagger paths.
  - If the endpoint includes templates (e.g. `{{input.id}}`), the path is normalized and matched against swagger `{param}` paths.
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
- Stage outputs, `set`, and `context`
- Init-stage and end-stage context
- Endpoint validation details

## Exit behavior

- Any validation error fails the dry-run and returns a non-zero exit code.
