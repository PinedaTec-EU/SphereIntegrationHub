# Variables and Context

This document explains variable scopes, context sharing, and random value generation.

## Variable scopes

### Inputs (`input`)

Inputs are provided by the caller via a `.wfvars` file. They can be referenced anywhere in the workflow:

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

### System datetime tokens

Use `{{system:datetime.now}}` or `{{system:datetime.utcnow}}` to insert the current timestamp (ISO 8601 format).
For date-only or time-only values, use:

- `{{system:date.now}}` / `{{system:date.utcnow}}` (format: `yyyy-MM-dd`)
- `{{system:time.now}}` / `{{system:time.utcnow}}` (format: `HH:mm:ss`)

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

Supported operators: `==`, `!=`, `in`, `not in`
Supported values: `null`, quoted strings, numeric literals, or numeric list literals (`[200, 201]`).

Examples:

```
runIf: "{{stage:create-account.output.http_status}} == 205"
runIf: "{{context.tokenId}} != \"\""
runIf: "{{stage:create-account.output.http_status}} in [500, 502, 503]"
runIf: "{{stage:create-account.output.http_status}} not in [500, 502, 503]"
```
