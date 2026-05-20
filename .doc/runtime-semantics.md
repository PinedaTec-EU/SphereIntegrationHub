# Workflow Runtime Semantics

This document collects the runtime rules that were previously embedded in the root `README.md`: workflow behavior, token resolution, `forEach`, outputs, and reporting semantics.

## Workflow overview

SphereIntegrationHub workflows are plain YAML and are designed to stay readable even when orchestration becomes complex.

- Reference APIs from the versioned catalog and compose parent and child workflows.
- Accept typed inputs, `.wfvars`, and `.env` values.
- Mix endpoint calls, workflow stages, retries, circuit breakers, delays, and conditional branches.
- Add LLM or SLM stages through the OpenAI plugin, including prompt files, JSON schema output, token limits, timeout, and usage reporting.
- Work with structured JSON, file-backed payloads, `forEach`, and idempotent `ensure` flows.
- Generate inline fake data with `{{rand:*}}` helpers for numbers, text, dates, datetimes, times, GUIDs, and ULIDs.
- Emit JSON and HTML execution reports for post-run diagnosis.

Related references:

- [`workflow-schema.md`](workflow-schema.md)
- [`variables.md`](variables.md)
- [`execution-reporting.md`](execution-reporting.md)

## Token and workflow semantics

Agents generating workflows should assume these runtime rules:

- `{{response.status}}`, `{{response.body}}`, and `{{response.headers.HeaderName}}` are supported for endpoint stages.
- `{{response.body.some.path}}` and `{{response.some.path}}` are valid when the response body is JSON.
- Optional path segments use a `?` suffix on the segment itself, for example `{{response.body.account.status?}}` or `{{stage:create.output.items.0.id?}}`. Missing optional segments resolve to empty output instead of failing.
- `runIf` supports compound expressions with `&&`, `||`, `!`, parentheses, safe comparisons against missing tokens, and helpers such as `exists(...)`, `empty(...)`, `coalesce(...)`, `first(...)`, `any(...)`, `jsonLength(...)`, and `isEmptyJson(...)`.
- `rand:*` helpers are valid inside template tokens and are evaluated at runtime wherever templates are supported. Examples: `{{rand:number(1,25)}}`, `{{rand:text(10, 'alnum')}}`, `{{rand:datetime(system:datetime.utcnow - P30D, system:datetime.utcnow)}}`.
- Workflow validation can check response token paths against endpoint mock payloads when `stage.mock.payload` or `stage.mock.payloadFile` is present.
- `kind: Workflow` stage failures propagate to the parent workflow; parent execution does not continue past a failed child workflow.
- `forEach` on workflow stages aggregates both outputs and result state. In addition to `foreach_count` and `foreach_items`, workflow stages expose `foreach_results`, `foreach_success_count`, and `foreach_failed_count`.
- `forEach` runs in parallel by default. Set `forEachSequential: true` on the stage when iterations must execute one by one.
- `response.*` tokens are endpoint-stage only. Workflow stages should use `stage:<name>.workflow.output.*` and `stage:<name>.workflow.result.{status,message}` instead.

## Examples

### Login workflow

```yaml
version: "3.10"
id: "01J7Z6J1KQZV8Y6J9G4E2ZB6QH"
name: "login"
description: "Login workflow that returns a JWT."
output: false
references:
  apis:
    - name: "example-service"
      definition: "example-service"
input:
  - name: "username"
    type: "Text"
    required: true
  - name: "password"
    type: "Text"
    required: true
stages:
  - name: "login"
    kind: "Endpoint"
    apiRef: "example-service"
    endpoint: "/api/auth/login"
    httpVerb: "POST"
    expectedStatus: 200
    headers:
      Content-Type: "application/json"
    body: |
      {
        "user": "{{input.username}}",
        "password": "{{input.password}}"
      }
    output:
      jwt: "{{response.jwt}}"
endStage:
  context:
    tokenId: "{{stage:login.output.jwt}}"
```

`output: false` only disables the `.workflow.output` file written to disk for that workflow execution. It does not suppress `endStage.output`, in-memory workflow outputs, or values exposed to parent workflows.

### Idempotent bootstrap with files and `forEach`

```yaml
version: "3.11"
id: "01JBOOTSTRAPEXAMPLE0000000001"
name: "bootstrap-accounts"
description: "Creates accounts idempotently from a seed file."
output: true
references:
  apis:
    - name: "accounts"
      definition: "accounts"
input:
  - name: "seed"
    type: "Array"
    required: true
stages:
  - name: "create-account"
    kind: "Endpoint"
    apiRef: "accounts"
    endpoint: "/api/accounts"
    httpVerb: "POST"
    expectedStatus: 201
    forEach: "{{input.seed}}"
    itemName: "item"
    bodyFile: "./payloads/create-account.json"
    ensure:
      mode: "CreateIfMissing"
      jumpTo: "load-existing"
      output:
        exists: "true"
  - name: "load-existing"
    kind: "Endpoint"
    apiRef: "accounts"
    endpoint: "/api/accounts/{{context:item.id}}"
    httpVerb: "GET"
    expectedStatus: 200
endStage:
  output:
    created: "{{stage:create-account.output.foreach_items}}"
```

Example `./payloads/create-account.json`:

```json
{
  "id": "{{context:item.id}}",
  "name": "{{context:item.name}}"
}
```

Sequential `forEach` when order matters:

```yaml
stages:
  - name: "publish-campaigns"
    kind: "Workflow"
    workflowRef: "campaign-item"
    forEach: "{{input.campaigns}}"
    forEachSequential: true
    itemName: "campaign"
    inputs:
      code: "{{context:campaign.code}}"
      targetStatus: "{{context:campaign.targetStatus}}"
```

## Execution reporting

SphereIntegrationHub can persist each run as JSON and HTML execution artifacts for diagnosis, auditability, and sharing.

- Machine-readable JSON report
- Self-contained HTML trace viewer
- Configurable HTTP capture with redaction by default
- Stage detail includes the `forEach` execution mode when iteration is enabled

The HTML viewer gives you a timeline, per-stage drill-down, and multi-run switching from the same page.

See [`execution-reporting.md`](execution-reporting.md) for full usage, artifact format details, and viewer behavior.
