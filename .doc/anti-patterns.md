# Anti-patterns

Workflows that compile and run successfully can still be wrong. This document covers recurring mistakes, their consequences, and the correct alternative.

---

## Control flow

### Using `jumpOnStatus` when `ensure` or `onStatus` is clearer

`jumpOnStatus` is a low-level escape hatch. When a stage semantically means "create if it doesn't exist", use `ensure`. When a non-happy path still produces output, use `onStatus`.

**Wrong:**

```yaml
- name: "create-account"
  kind: "Endpoint"
  httpVerb: "POST"
  expectedStatuses: [201, 409]
  jumpOnStatus:
    409: "load-existing"
  output:
    accountId: "{{response.body}}"
```

**Correct:**

```yaml
- name: "create-account"
  kind: "Endpoint"
  httpVerb: "POST"
  expectedStatus: 201
  ensure:
    mode: "CreateIfMissing"
    jumpTo: "load-existing"
    output:
      accountId: "{{response.body}}"
```

`ensure` adds semantic outputs (`ensure_status`, `ensured`, `existed`) automatically, and its intent is explicit to anyone reading the workflow.

---

### Using `onStatus` without `expectedStatuses`

If a status code is handled by `onStatus`, it must also be listed in `expectedStatuses`. Otherwise the runtime rejects it before `onStatus` can process it.

**Wrong:**

```yaml
expectedStatus: 200
onStatus:
  409:
    jumpTo: "load-existing"
```

**Correct:**

```yaml
expectedStatuses: [200, 409]
onStatus:
  409:
    jumpTo: "load-existing"
```

---

### Mixing `expectedStatus` and `expectedStatuses`

Use only one. `expectedStatuses` is the canonical field for multiple codes. Using both produces undefined behavior.

**Wrong:**

```yaml
expectedStatus: 200
expectedStatuses: [200, 201]
```

**Correct:**

```yaml
expectedStatuses: [200, 201]
```

---

### Reading `{{response.*}}` outside of endpoint stage output

`response.*` tokens are only available inside `stage.output`, `stage.message`, and `onStatus.output`. Using them in `runIf`, `context`, or `set` silently resolves to empty.

**Wrong:**

```yaml
context:
  lastStatus: "{{response.status}}"  # not available here
runIf: "{{response.body}} != null"   # always false
```

**Correct:**

```yaml
output:
  statusCode: "{{response.status}}"
  body: "{{response.body}}"
```

Then reference via `{{stage:my-stage.output.statusCode}}` in subsequent stages.

---

### Ignoring `circuitBreaker` dependency on `retry`

`circuitBreaker` requires a `retry` definition on the same stage. Without `retry`, the circuit breaker has no configured HTTP statuses to track and is silently ignored.

**Wrong:**

```yaml
circuitBreaker:
  ref: "default"
```

**Correct:**

```yaml
retry:
  ref: "standard"
  httpStatus: [500, 503]
circuitBreaker:
  ref: "default"
```

---

### Retrying business statuses such as `404` on lookup steps

When a GET or search stage can legitimately return "not found", that is a business outcome, not a transient transport failure. Retrying it wastes time and hides intent.

**Wrong:**

```yaml
- name: "lookup-account"
  kind: "Endpoint"
  httpVerb: "GET"
  expectedStatus: 200
  retry:
    ref: "standard"
    httpStatus: [404, 500, 503]
```

**Correct:**

```yaml
- name: "lookup-account"
  kind: "Endpoint"
  httpVerb: "GET"
  expectedStatuses: [200, 404]
  onStatus:
    404:
      output:
        found: "false"
```

Reserve `retry` for transient failures, and handle domain-level misses with `expectedStatuses`, `onStatus`, or `ensure`.

---

## Variable and context scoping

### Using `context` as a general-purpose store

`context` is shared between parent and child workflows. Storing stage-local data in it pollutes the shared namespace and can cause subtle conflicts in multi-workflow compositions.

**Wrong:**

```yaml
context:
  tempAccountId: "{{response.body}}"
  currentIndex: "{{context:index}}"
```

**Correct:**

```yaml
output:
  accountId: "{{response.body}}"
```

Use `output` for data that only subsequent stages need. Use `context` only for values that child workflows must read.

---

### Setting `context` in `initStage` unconditionally

`initStage.context` only sets a key **if it does not already exist**. This is by design to avoid overwriting a value set by a parent workflow. Assuming `initStage.context` always overwrites is a common source of hard-to-trace bugs.

If you need to always set a value at stage initialization, use `initStage.variables` + `set`.

---

### Using `set` to pass data to child workflows

`set` updates workflow globals (`{{global.*}}`), which are scoped to the current workflow only. Child workflows cannot read them. Use `context` for cross-workflow state, or pass data explicitly via `inputs`.

**Wrong (parent workflow):**

```yaml
set:
  accountId: "{{stage:create.output.id}}"

- name: "process"
  kind: "Workflow"
  workflowRef: "process-account"
```

**Correct:**

```yaml
- name: "process"
  kind: "Workflow"
  workflowRef: "process-account"
  inputs:
    accountId: "{{stage:create.output.id}}"
```

---

### Hardcoding environment-specific values in the workflow body

Credentials, base URLs, and environment flags embedded directly in the workflow break when the workflow runs in a different environment.

**Wrong:**

```yaml
body: |
  { "org": "acme-pre", "token": "Bearer abc123" }
```

**Correct:**

```yaml
# In .wfvars or via {{env:ORG_NAME}}
body: |
  { "org": "{{env:ORG_NAME}}", "token": "Bearer {{input.token}}" }
```

---

### Referencing a stage output before the stage has executed

Token validation does not enforce ordering. A stage that reads `{{stage:login.output.jwt}}` before `login` has executed will silently receive an empty string.

**Wrong:**

```yaml
stages:
  - name: "create-account"
    body: |
      { "token": "{{stage:login.output.jwt}}" }

  - name: "login"
    httpVerb: "POST"
    endpoint: "/auth/token"
```

**Correct:** ensure the producing stage (`login`) appears before the consuming stage (`create-account`).

---

## JSON payloads and body

### Using `{{response.body}}` directly as a request body

`{{response.body}}` is a raw JSON string. Passing it directly as a body without extracting fields works only if the downstream API accepts the exact shape returned by the upstream call. In all other cases, use `stage:json(...)` to extract specific fields.

**Wrong:**

```yaml
body: "{{stage:create-account.output.dto}}"
```

**Correct (when you need a specific field):**

```yaml
body: |
  { "id": "{{stage:json(create-account.output.dto).id}}" }
```

---

### Using both `payload` and `payloadFile` in a mock

Only one is accepted. `payloadFile` takes precedence; `payload` is ignored.

**Wrong:**

```yaml
mock:
  status: 200
  payload: |
    { "id": "1" }
  payloadFile: "./mocks/account.json"
```

**Correct:** use one or the other.

---

## Workflow composition

### One monolithic workflow for multi-step operations

A single workflow with 15+ stages that covers login, data setup, and validation is difficult to reuse and impossible to run partially. Compose instead.

**Wrong:** one workflow with stages: `login → create-org → create-user → assign-role → validate`.

**Correct:** separate `login.workflow` + `setup-org.workflow` that calls `login` and creates resources. Each can be run and mocked independently.

---

### Using `allowVersion` to silence every version mismatch

`allowVersion` exists for deliberate cross-version compositions. Using it everywhere to suppress the error means silent runtime incompatibilities when the referenced workflow changes.

**Wrong:**

```yaml
- name: "login"
  kind: "Workflow"
  workflowRef: "login"
  allowVersion: "*"
```

**Correct:** either align the versions or use `allowVersion` explicitly with the exact version you have verified against:

```yaml
allowVersion: "3.10"
```

---

## Inputs and wfvars

### Declaring `required: true` inputs with no `.wfvars` file

A workflow with required inputs and no `.wfvars` sibling file will fail immediately with a missing-input error. The file does not need values — it can contain placeholders — but it must exist for documentation and CI purposes.

Required inputs without a `.wfvars` are also invisible to tooling and the MCP server.

---

### Using `input` for values that never change between runs

If a value is the same across all runs in an environment (service name, base path, flag), it should be an `env` var or a `Fixed` global, not an input. Exposing it as an input adds mandatory caller overhead with no benefit.

**Wrong:**

```yaml
input:
  - name: "serviceName"
    type: "Text"
    required: true
```

**Correct:**

```yaml
initStage:
  variables:
    - name: "serviceName"
      type: "Fixed"
      value: "{{env:SERVICE_NAME}}"
```

---

## Output and observability

### Setting `output: true` on every workflow

`output: true` writes a `.workflow.output` file after each run. On workflows that run frequently or in batch, this creates noise and disk churn. Enable it only when the output file is actively consumed.

---

### Empty `debug` blocks or `message` on every stage

`debug` runs before stage invocation (no response tokens available). Logging `{{response.body}}` in `debug` silently emits nothing. Reserve `debug` for pre-invocation diagnostics and `message` for post-invocation confirmations.

**Wrong:**

```yaml
debug:
  result: "{{response.body}}"   # always empty
```

**Correct:**

```yaml
output:
  result: "{{response.body}}"
message: "Account created: {{stage:create-account.output.result}}"
```
