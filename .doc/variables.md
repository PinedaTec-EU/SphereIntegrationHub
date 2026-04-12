# Variables and Context

This document explains variable scopes, context sharing, and random value generation.

## Variable scopes

### Inputs (`input`)

Inputs are provided by the caller via a `.wfvars` file. They can be referenced anywhere in the workflow.

Mark an input as `secret: true` to prevent its value from appearing in the execution report:

```yaml
input:
  - name: "apiKey"
    type: "Text"
    required: true
    secret: true
```

Behavior:

- The value is masked as `*****` in the `Inputs` section of the execution report.
- The value is registered in the runtime secret register and masked wherever it appears in stage outputs.
- The input name is always visible.

---

Inputs are provided by the caller via a `.wfvars` file:

```
username: boss
password: secret
accountName: Acme
```

```
{{input.username}}
```

Use `--varsfile <path>` to pass a specific vars file, or place `{workflow}.wfvars` alongside the workflow for auto-detection.

Format notes:

- One `key: value` per line.
- Lines starting with `#` are ignored.
- You can group variables by environment and (optionally) version.
- `global` is the default if no environment sections are defined or if the requested environment is missing.

Example `.wfvars`:

```yaml
# auth
jwt: "mocked-jwt-token"

# account
accountLogin: "sample.user@example.com"
accountPassword: "PinedaTest$Pwd|123"
```

Environment-aware `.wfvars`:

```yaml
global:
  jwt: "default-jwt"
  accountLogin: "sample.user@example.com"

pre:
  # no version specified
  accountPassword: "PrePwd$123"
  
  version: 3.10
  accountPassword: "PrePwd$310"

  version: 3.11
  accountPassword: "PrePwd$311"
  exclusive311: "somevalue"
```

Resolution rules:

- Global variables apply everywhere unless overridden by an environment.
- Environment variables override global values.
- Version variables override environment values when the version matches.

### Globals (`initStage.variables` and `set`)

Globals belong to the current workflow execution. They can be set in:

- `initStage.variables` (initialization)
- `set` (stage-level update)

References use:

```
{{global.someName}}
```

#### Secret variables

Add `secret: true` to an `initStage.variables` entry to mark the generated value as sensitive:

```yaml
initStage:
  variables:
    - name: "nonce"
      type: "Guid"
      secret: true
```

Behavior:

- The real value is kept in the execution context for template resolution — workflows operate normally.
- The generated value is registered in the runtime secret register.
- Any stage output or workflow output whose resolved value matches a registered secret value is automatically masked as `*****` in the execution report.

### Shared context (`context`)

Context is shared between parent and child workflows. It can be:

- Initialized in `initStage.context` (only if the key does not exist).
- Updated in a stage via `context` (merges into the shared context).
- Updated in `endStage.context`.

References use:

```
{{context.tokenId}}
```

Context lets you pass tokens (JWTs) between workflows without repeated login calls.

### Environment variables (`env`)

Environment variables are read from `.env` files and the process environment (fallback) and can be referenced anywhere:

```
{{env:ORGANIZATION}}
```

Use them to seed globals or stage values:

```yaml
initStage:
  variables:
    - name: "envOrganization"
      type: "Fixed"
      value: "{{env:ORGANIZATION}}"
```

Environment file resolution:

- `.env` format is `KEY=value` per line (comments start with `#`).
- `references.environmentFile` loads a `.env` file for the workflow.
- Child workflows can define their own `references.environmentFile`; values are merged, and parent values win on conflicts.
- If a key is missing in `.env`, the process environment is used as a fallback.
- The CLI `--envfile` option overrides the root workflow's `references.environmentFile`.

### Variable-based workflow paths

Referenced workflow paths also support template resolution. This allows selecting child workflows by environment or business inputs:

```yaml
references:
  environmentFile: "./.env"
  workflows:
    - name: "tenant-child"
      path: "./{{env:TENANT}}/child.workflow"
```

```yaml
references:
  workflows:
    - name: "tenant-child"
      path: "./{{input.tenant}}/child.workflow"
```

Notes:

- Path templates are expanded before resolving the final path relative to the current workflow file.
- `{{env:NAME}}` works during load/validation when the environment value is already available.
- `{{input.*}}`, `{{global.*}}`, and `{{context.*}}` are supported at runtime for nested workflow execution. For CLI validation, `{{input.*}}` requires that the value is already provided, for example via `.wfvars` or `--varsfile`.
- The same approach also applies to `bodyFile`, `dataFile`, and `mock.payloadFile`.
- `references.environmentFile` also supports templates, but it is resolved earlier in the lifecycle, so in practice it should depend on values already available at load time, typically `env` or `system`.

Resolution timing matters:

- `references.workflows[].path` in a `kind: Workflow` stage is resolved again at execution time, right before the child workflow is loaded. That means runtime values such as `input`, `global`, and `context` can be used for real execution.
- Validation and `--dry-run` may also resolve and load referenced workflows earlier to inspect inputs, versions, nested plans, and referenced files.
- When a path depends on business values that are not available yet, `--dry-run` does not fail just because of that. Instead, it emits a warning saying that the path will be resolved at runtime.
- Runtime is still strict: if the path cannot be resolved when the stage actually runs, execution fails with a stage-specific error that includes the original path expression.
- Because of that, `env` tokens are the safest choice for reusable paths. `input` is also valid when the caller has already provided the value. `global` and `context` remain runtime-oriented values.

### System datetime tokens

Use `{{system:datetime.now}}` or `{{system:datetime.utcnow}}` to insert the current timestamp (ISO 8601 format).
For date-only or time-only values, use:

- `{{system:date.now}}` / `{{system:date.utcnow}}` → `yyyy-MM-dd`
- `{{system:time.now}}` / `{{system:time.utcnow}}` → `HH:mm:ss`

#### Date/time offsets

You can add or subtract a duration using [ISO 8601 duration](https://en.wikipedia.org/wiki/ISO_8601#Durations) syntax
([spec reference](https://www.iso.org/iso-8601-date-and-time-format.html)):

```
{{ system:<datetime|date|time>.<now|utcnow> +|- P[nY][nM][nD][T[nH][nM][nS]] }}
```

The `P` prefix is required. Include only the components you need — all are optional:

| Component | Meaning         | Example |
|-----------|-----------------|---------|
| `nY`      | Years           | `P2Y` = 2 years |
| `nM`      | Months (before `T`) | `P6M` = 6 months |
| `nD`      | Days            | `P10D` = 10 days |
| `T`       | Time separator  | required before any time component |
| `nH`      | Hours           | `PT3H` = 3 hours |
| `nM`      | Minutes (after `T`) | `PT30M` = 30 minutes |
| `nS`      | Seconds         | `PT45S` = 45 seconds |

Examples:

```yaml
# Expiry date 30 days from today
expiresAt: "{{system:date.utcnow + P30D}}"

# Datetime 1 hour and 30 minutes in the future
scheduledAt: "{{system:datetime.utcnow + PT1H30M}}"

# Date 1 year and 6 months from now
renewalDate: "{{system:date.now + P1Y6M}}"

# Yesterday
yesterday: "{{system:date.utcnow - P1D}}"

# Full offset: +1 year, 2 months, 3 days, 4 hours, 5 minutes, 6 seconds
fullOffset: "{{system:datetime.now + P1Y2M3DT4H5M6S}}"
```

## Stage outputs

### Endpoint stage output

Each endpoint stage can map response values:

```
output:
  jwt: "{{response.jwt}}"
```

Reference it with:

```
{{stage:login.output.jwt}}
```

To extract fields from a JSON string output:

```
{{stage:json(login.output.jwt).id}}
```

### Workflow stage output

Workflow outputs from `endStage.output` are referenced as:

```
{{stage:login.output.jwt}}
```

## Random values

`initStage.variables` can generate random values using `RandomValueType`. For example:

```yaml
initStage:
  variables:
    - name: "ticketNumber"
      type: "Number"
      min: 1000
      max: 9999
    - name: "trackingId"
      type: "Guid"
    - name: "createdAt"
      type: "DateTime"
```

### Fixed values

Use `Fixed` to set a literal value:

```yaml
initStage:
  variables:
    - name: "issueNumber"
      type: "Fixed"
      value: "000000001972"
```

If `value` contains templates, they are resolved first.

## Supported RandomValueType values

- `Fixed`
- `Number`
- `Text`
- `Guid`
- `Ulid`
- `DateTime`
- `Date`
- `Time`
- `Sequence`

## Variable type details

Each `initStage.variables` entry has:

- Required: `name`, `type`
- Optional: `value` (Fixed only; templates allowed)

### Fixed

Literal value. Use this when you want an exact string, or when you want to resolve a template into a single value.

- Required: `value`
- Optional: none

```yaml
- name: "issueNumber"
  type: "Fixed"
  value: "000000001972"
```

### Number

Random integer in a range. Both `min` and `max` are inclusive. If only one bound is provided, the other uses the default.
`padding` left-pads the number with zeros to the specified width.

- Optional: `min`, `max`, `padding`

```yaml
- name: "ticketNumber"
  type: "Number"
  min: 1000
  max: 9999
  padding: 6
```

### Text

Random alphanumeric string (A–Z, a–z, 0–9). Use `length` to control the number of characters.

- Optional: `length`

```yaml
- name: "randomKey"
  type: "Text"
  length: 24
```

### Guid

Random GUID generated by the runtime. Useful for correlation or idempotency.

- Optional: none

```yaml
- name: "correlationId"
  type: "Guid"
```

### Ulid

Random ULID, lexicographically sortable by generation time.

- Optional: none

```yaml
- name: "eventId"
  type: "Ulid"
```

### DateTime

Random date/time within a range. Bounds are inclusive. `format` follows .NET date/time format strings.

- Optional: `fromDateTime`, `toDateTime`, `format`
- Defaults: if neither `fromDateTime` nor `toDateTime` is provided, range is `UtcNow - 1 month` to `UtcNow + 1 month`. If only `fromDateTime` is provided, `toDateTime` defaults to `fromDateTime + 1 month`. If only `toDateTime` is provided, `fromDateTime` defaults to `toDateTime - 1 month`.
- `value` is not supported for `DateTime`. Use `type: Fixed` with a datetime value instead.

```yaml
- name: "createdAt"
  type: "DateTime"
  fromDateTime: "2024-01-01T00:00:00Z"
  toDateTime: "2025-01-01T00:00:00Z"
  format: "O"
```

### Date

Random date within a range. Bounds are inclusive. `format` follows .NET date format strings.

- Optional: `fromDate`, `toDate`, `format`
- Defaults: if neither `fromDate` nor `toDate` is provided, range is `UtcNow - 1 month` to `UtcNow + 1 month`. If only `fromDate` is provided, `toDate` defaults to `fromDate + 1 month`. If only `toDate` is provided, `fromDate` defaults to `toDate - 1 month`.
- `value` is not supported for `Date`. Use `type: Fixed` with a date value instead.

```yaml
- name: "birthDate"
  type: "Date"
  fromDate: "1980-01-01"
  toDate: "2000-12-31"
  format: "yyyy-MM-dd"
```

### Time

Random time within a range. Bounds are inclusive. `format` follows .NET time format strings.

- Optional: `fromTime`, `toTime`, `format`
- `value` is not supported for `Time`. Use `type: Fixed` with a time value instead.

```yaml
- name: "meetingTime"
  type: "Time"
  fromTime: "08:00:00"
  toTime: "18:00:00"
  format: "HH:mm:ss"
```

### Sequence

Deterministic sequence based on the execution index. `start` is the first value, `step` is the increment. `padding` left-pads with zeros.

- Optional: `start`, `step`, `padding`
- Requires a valid execution context (sequence uses the current index).

```yaml
- name: "sequenceId"
  type: "Sequence"
  start: 1
  step: 1
  padding: 6
```

## Conditionals (`runIf`)

Stages support conditional execution:

```
runIf: "{{context.tokenId}} == null"
```

Supported operators include `==`, `!=`, `>`, `>=`, `<`, `<=`, `in`, `not in`, `&&`, `||`, and `!`.
Parentheses are supported, and comparisons against unresolved tokens are safe: missing tokens behave like `null` instead of failing the stage condition.

Supported helper functions:

- `exists(value)`
- `empty(value)`
- `coalesce(value, fallback, ...)`
- `isEmptyJson(value)`
- `jsonLength(value)`
- `first(value)`
- `any(value)`

Examples:

```
runIf: "{{stage:create-account.output.http_status}} == 205"
runIf: "{{context.tokenId}} != \"\""
runIf: "{{stage:create-account.output.http_status}} in [500, 502, 503]"
runIf: "{{stage:create-account.output.http_status}} not in [500, 502, 503]"
runIf: "jsonLength({{input.items}}) > 0"
runIf: "exists(first({{input.items}})) && !isEmptyJson({{response.body}})"
runIf: "coalesce({{stage:create-account.output.accountId}}, {{context.accountId}}, 'pending') != 'pending'"
runIf: "empty({{stage:create-account.output.accountId}}) || ({{stage:create-account.output.http_status}} == 200 && {{context.tokenId}} == null)"
```
