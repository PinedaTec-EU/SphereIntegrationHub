# Workflow Schema

Workflows are YAML files with extension `.workflow`. Each workflow has a unique ULID in `id`.

## Top-level fields

- `version` (string, required): Catalog version to use.
- `id` (string, required): ULID for the workflow.
- `name` (string, required): Workflow name.
- `description` (string, optional): Human description.
- `output` (bool, optional): Whether to write output file (`{name}.{executionId}.workflow.output`).
- `references` (object, optional): External workflow and API references.
- `input` (array, optional): Required inputs from the caller.
- `initStage` (object, optional): Workflow-specific variables and context defaults.
- `resilience` (object, optional): Shared retry/circuit breaker policies.
- `stages` (array, optional): Endpoint or workflow stages.
- `endStage` (object, optional): Workflow output and context updates.

## references

```yaml
references:
  workflows:
    - name: "login"
      path: "./login.workflow"
  apis:
    - name: "example-service"
      definition: "example-service"
  environmentFile: "./.env"
```

- `workflows`: list of referenced workflows (name + relative path).
- `apis`: list of API definitions (name + definition in catalog).
- `environmentFile`: optional `.env` file (key=value) loaded for `{{env:NAME}}` resolution.

Child workflows can define their own `environmentFile`; parent values override child values on conflicts.

## input

```yaml
input:
  - name: "username"
    type: "Text"
    required: true
  - name: "payload"
    type: "Object"
    required: true
  - name: "items"
    type: "Array"
    required: false
  - name: "apiKey"
    type: "Text"
    required: true
    secret: true
```

`type` reuses `RandomValueType` enum values (Text, Number, Guid, Object, Array, etc.).

Fields:

- `name` (string, required): input name.
- `type` (string, optional): value type hint.
- `required` (bool, default `true`): whether the input is required.
- `secret` (bool, default `false`): when `true`, the input value is masked as `*****` in the execution report `Inputs` section. The name is always visible. The value is also added to the runtime secret register so it is masked wherever it appears in stage outputs.

## initStage

```yaml
initStage:
  variables:
    - name: "accountName"
      type: "Fixed"
      value: "{{input.accountName}}"
  context:
    tokenId: "{{input.tokenId}}"
```

- `variables`: workflow globals. Use when you need derived or fixed values.
- `context`: initial shared context values. **Only set if the key is missing.**

## resilience

Define reusable retry and circuit breaker policies:

```yaml
resilience:
  retries:
    standard:
      maxRetries: 3
      delayMs: 250
  circuitBreakers:
    default:
      failureThreshold: 5
      breakMs: 30000
```

Policy names are the map keys (`standard`, `default`) and are referenced from stages via `retry.ref` and `circuitBreaker.ref`. No extra `name` field is required.
`circuitBreaker` is optional; stages can use only `retry` if needed.
Messages are defined per stage under `retry.messages` and `circuitBreaker.messages`.

## stages

All stages accept `delaySeconds` (int, optional). Values must be between 0 and 60. Defaults to 0.

### Endpoint stage

```yaml
stages:
  - name: "create-account"
    kind: "Endpoint"
    apiRef: "example-service"
    endpoint: "/api/accounts"
    httpVerb: "POST"
    expectedStatus: 201
    # or: expectedStatuses: [200, 201, 409]
    headers:
      Authorization: "Bearer {{context.tokenId}}"
    delaySeconds: 2
    body: |
      { "name": "{{global:accountName}}" }
    # or: bodyFile: "./payloads/create-account.json"
    debug:
      user: "{{input.username}}"
      token: "{{context.tokenId}}"
    message: "Login succeeded for {{input.username}}"
    output:
      dto: "{{response.body}}"
      accessToken: "{{stage:json(create-account.output.dto).token}}"
    secretOutputs:
      - accessToken   # value masked as ***** in the execution report; name is still visible
    # optional collection iteration
    # forEach: "{{input.items}}"
    # dataFile: "./seed/accounts.json"
    # itemName: "item"
    # indexName: "index"
    retry:
      ref: "standard"
      httpStatus: [500, 503]
    circuitBreaker:
      ref: "default"
      httpStatus: [500, 503]
```

Mocking (only applied when CLI uses `--mocked`):

```yaml
stages:
  - name: "create-account"
    kind: "Endpoint"
    apiRef: "example-service"
    endpoint: "/api/accounts"
    httpVerb: "POST"
    expectedStatus: 201
    mock:
      status: 201
      payloadFile: "./mocks/create-account.json"
```

Inline JSON payloads are also supported:

```yaml
mock:
  status: 201
  payload: |
    {
      "id": "{{input.accountId}}",
      "name": "{{input.accountName}}"
    }
```

Use either `payload` or `payloadFile` (not both).

#### Retry (endpoint only)

```yaml
retry:
  ref: "standard"
  httpStatus: [500, 501, 503]
  maxRetries: 3
  delayMs: 250
  messages:
    onException: "Stage failed after retries."
```

- `ref`: optional reference to `resilience.retries`.
- `httpStatus`: required list of status codes that trigger a retry.
- `maxRetries`: override for max retry count (uses `resilience.retries` when omitted).
- `delayMs`: override for delay between retries (uses `resilience.retries` when omitted).
- `messages.onException`: optional message when the stage exhausts retries due to an exception.
- Retries apply to matching HTTP statuses and exceptions thrown during invocation.
- If defined on a workflow stage, it is ignored with a warning.

#### Circuit breaker (endpoint only)

```yaml
circuitBreaker:
  ref: "default"
  failureThreshold: 5
  breakMs: 30000
  closeOnSuccessAttempts: 1
  messages:
    onOpen: "Circuit opened."
    onBlocked: "Circuit open. Skipping call."
```

- `ref`: optional reference to `resilience.circuitBreakers`.
- `httpStatus` comes from the paired `retry` definition.
- `failureThreshold`: override for consecutive failures before opening (uses `resilience.circuitBreakers` when omitted).
- `breakMs`: override for how long to keep the breaker open (uses `resilience.circuitBreakers` when omitted).
- `messages.onOpen`: optional message when the breaker opens.
- `messages.onBlocked`: optional message when a stage is blocked because the breaker is open.
- `closeOnSuccessAttempts`: successes required to fully close the breaker after timeout (default `1`).
- The breaker opens after a failed execution (post-retry) reaches `failureThreshold`.
- After `breakMs` expires, the breaker is half-open; it closes after `closeOnSuccessAttempts` successful executions.
- `retry` is required when `circuitBreaker` is defined.
- If defined on a workflow stage, it is ignored with a warning.

### Workflow stage

```yaml
stages:
  - name: "login"
    kind: "Workflow"
    workflowRef: "login"
    allowVersion: "3.10"
    delaySeconds: 2
    inputs:
      username: "{{input.username}}"
      password: "{{input.password}}"
```

Workflow mocks skip executing the referenced workflow and return the declared output:

```yaml
stages:
  - name: "login"
    kind: "Workflow"
    workflowRef: "login"
    mock:
      output:
        jwt: "{{context.tokenId}}"
```

Common fields:

- `runIf`: conditional execution with comparisons, boolean operators, parentheses, safe missing-token checks, and helper functions. Examples:
  - `{{context.tokenId}} == null`
  - `{{context.tokenId}} != ""`
  - `{{stage:create-account.output.http_status}} in [505, 200, 201]`
  - `exists({{context:item}}) && !isEmptyJson({{response.body}})`
  - `empty({{stage:create-account.output.accountId}}) || {{context.tokenId}} == null`
  - `coalesce({{stage:create-account.output.accountId}}, {{context.accountId}}, 'pending') != 'pending'`
  - `jsonLength({{input.items}}) > 0`
  - `exists(first({{input.items}}))`
- `debug`: key/value map printed before stage invocation when `--debug` is enabled (response tokens are not available).
- `message`: printed after a stage completes successfully (response tokens are available for endpoint stages).
- `output`: key/value map of resolved output values captured after the stage executes.
- `secretOutputs`: list of `output` key names whose values are masked as `*****` in the execution report. The key name is always visible.
- `context`: stage-level context updates (merges into shared context).
- `set`: stage-level global updates (merges into workflow globals).
- `jumpOnStatus` (endpoint only): status-based jump, including `endStage`.
- `onStatus` (endpoint only): status-based branch with optional `jumpTo`, branch-local `output`, optional `message`, and optional `fail`.
- `ensure` (endpoint only): semantic sugar for idempotent create/bootstrap flows.
- `expectedStatuses` (endpoint only): accepted status codes list. Use this for idempotent bootstrap flows.
- `bodyFile` (endpoint only): loads request body from a relative or absolute file.
- `dataFile` + `forEach`: iterate endpoint/workflow stages over a JSON/YAML array.
- `itemName` / `indexName`: names exposed into `context` during `forEach` iteration.
- `retry` (endpoint only): retries on configured HTTP status codes.
- `circuitBreaker` (endpoint only): short-circuits calls after repeated failures.

### jumpOnStatus details

Use `jumpOnStatus` to change control flow based on the HTTP status code:

```yaml
jumpOnStatus:
  200: "next-stage"
  404: "create-account"
  409: "endStage"
```

Rules:

- Keys are numeric HTTP status codes.
- Values are stage names or `endStage`.
- Only available for `Endpoint` stages.

Behavior with `expectedStatus` / `expectedStatuses`:

- `onStatus` is evaluated first.
- Then `expectedStatuses` / `expectedStatus` are enforced.
- Then legacy `jumpOnStatus` is applied when present.
- This allows patterns like `expectedStatuses: [200, 201, 409]` plus `onStatus.409.jumpTo`.

### onStatus details

Use `onStatus` when a non-happy path should still produce outputs or branch without failing:

```yaml
expectedStatuses: [200, 201, 409]
onStatus:
  409:
    jumpTo: "load-existing"
    output:
      exists: "true"
      conflictBody: "{{response.body}}"
```

Rules:

- Keys are numeric HTTP status codes.
- `jumpTo` accepts a stage name or `endStage`.
- `output` is merged into the stage output for that branch.
- `fail: true` forces the branch to fail after branch outputs are computed.

### ensure details

Use `ensure` when the stage semantically means "create if missing" or "bootstrap idempotently":

```yaml
expectedStatus: 201
ensure:
  mode: "CreateIfMissing"
  jumpTo: "load-existing"
  output:
    exists: "true"
```

Rules:

- `ensure` is only supported on `Endpoint` stages.
- Supported modes: `CreateIfMissing`, `Upsert`.
- `existsOn` defaults to `[409]`.
- `jumpTo` accepts a stage name or `endStage`.
- `output` is merged only for the "already existed" branch.
- Runtime automatically treats `existsOn` statuses as allowed, even if not listed in `expectedStatus` / `expectedStatuses`.
- Runtime automatically adds semantic outputs:
  - `ensure_status`: `created` or `existing`
  - `ensured`: `true`
  - `existed`: `true` or `false`

Equivalent explicit form:

```yaml
expectedStatuses: [201, 409]
onStatus:
  409:
    jumpTo: "load-existing"
    output:
      exists: "true"
```

### foreach and dataFile

Use `forEach` when the stage should run once per array item:

```yaml
forEach: "{{input.items}}"
itemName: "item"
indexName: "index"
body: |
  {
    "id": "{{context:item.id}}",
    "position": "{{context:index}}"
  }
```

`dataFile` can provide the collection directly:

```yaml
dataFile: "./seed/accounts.json"
forEach: "accounts"
```

If `dataFile` is present:

- JSON and YAML are supported.
- `forEach` is optional; when present it is treated as a path inside the loaded document.
- Stage outputs expose `foreach_count` and `foreach_items`.

## endStage

```yaml
endStage:
  output:
    account: "{{stage:create-account.output.dto}}"
    accessToken: "{{stage:login.output.jwt}}"
  secretOutputs:
    - accessToken   # value masked as ***** in the execution report; name is still visible
  context:
    tokenId: "{{stage:login.output.jwt}}"
  result:
    message: "Workflow completed successfully."
```

- `output`: workflow output (referenced by parent workflows).
- `secretOutputs` (list of strings, optional): output keys whose values are masked as `*****` in the execution report. The key name remains visible.
- `outputJson`: when `true` (default), output values that are JSON objects/arrays are emitted as JSON; when `false`, values are serialized as strings.
- `context`: updates shared context after workflow completion.
- `result.message`: optional success message for `workflow.result.message`.

## Template tokens

Supported tokens:

- `{{input.name}}`
- `{{global.name}}`
- `{{context.name}}`
- `{{env:NAME}}`
- `{{system:datetime.now}}`
- `{{system:datetime.utcnow}}`
- `{{system:date.now}}`
- `{{system:date.utcnow}}`
- `{{system:time.now}}`
- `{{system:time.utcnow}}`
- `{{stage:stage.output.key}}`
- `{{stage:json(stage.output.key).path}}` (parse JSON string output and navigate properties)
- `{{stage:<stage>.output.http_status}}` (endpoint-only automatic output; use a different key to avoid overriding)
- `{{stage:<stage>.workflow.result.status}}` (workflow stage result status: `Ok`/`Error`)
- `{{stage:<stage>.workflow.result.message}}` (workflow stage result message)
- `{{stage:<stage>.workflow.output.key}}` (workflow stage output)
- `{{response.status}}`, `{{response.body}}`, `{{response.headers.HeaderName}}`

## Token visibility by section (validation rules)

Use these tables to decide which tokens are valid for each section.

Legend:
- ✅ allowed and validated in that section.
- ❌ rejected in that section.

### Endpoint stages

| Section | input | global | context | env | system | stage.* | response.* |
| --- | --- | --- | --- | --- | --- | --- | --- |
| initStage.variables.value | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ |
| initStage.context | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ |
| stage.headers/query/body | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ |
| stage.debug | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ |
| stage.message | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| stage.output | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| stage.set | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ |
| stage.context | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ |
| stage.runIf | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ |
| stage.mock.payload/output | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ |
| endStage.output | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ |
| endStage.context | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ |
| endStage.result.message | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ |

### Workflow stages

| Section | input | global | context | env | system | stage.* |
| --- | --- | --- | --- | --- | --- | --- |
| initStage.variables.value | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| initStage.context | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| stage.inputs | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| stage.debug | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| stage.message | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| stage.output | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| stage.set | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| stage.context | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| stage.runIf | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| stage.mock.payload/output | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| endStage.output | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| endStage.context | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| endStage.result.message | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |

Note: `response.*` is not accessible from workflow stages.

Notes:
- `stage.*` refers to `{{stage:<stage>.output.*}}` and workflow stage output/result tokens.
- Token validation does not enforce stage ordering; only use stage outputs after the producing stage has executed.

## Version override

When a workflow references another workflow with a different version:

- By default, dry-run and execution fail.
- Use `allowVersion` on the workflow stage to allow a different version explicitly.
