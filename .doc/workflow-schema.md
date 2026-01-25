# Workflow Schema

Workflows are YAML files with extension `.workflow`. Each workflow has a unique ULID in `id`.

## Top-level fields

- `version` (string, required): Catalog version to use.
- `id` (string, required): ULID for the workflow.
- `name` (string, required): Workflow name.
- `description` (string, optional): Human description.
- `output` (bool, optional): Whether to write output file (`{name}.{id}.workflow.output`).
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
```

`type` reuses `RandomValueType` enum values (Text, Number, Guid, etc.).

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
    headers:
      Authorization: "Bearer {{context.tokenId}}"
    delaySeconds: 2
    body: |
      { "name": "{{global:accountName}}" }
    debug:
      user: "{{input.username}}"
      token: "{{context.tokenId}}"
    message: "Login succeeded for {{input.username}}"
    output:
      dto: "{{response.body}}"
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

- `runIf`: conditional execution. Supported forms:
  - `{{context.tokenId}} == null`
  - `{{context.tokenId}} != ""`
  - `{{stage:create-account.output.http_status}} == 205`
  - `{{stage:create-account.output.http_status}} in [505, 200, 201]`
  - `{{stage:create-account.output.http_status}} not in [505, 200, 201]`
- `debug`: key/value map printed before stage invocation when `--debug` is enabled (response tokens are not available).
- `message`: printed after a stage completes successfully (response tokens are available for endpoint stages).
- `context`: stage-level context updates (merges into shared context).
- `set`: stage-level global updates (merges into workflow globals).
- `jumpOnStatus` (endpoint only): status-based jump, including `endStage`.
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

Behavior with `expectedStatus`:

- If the response status matches a `jumpOnStatus` entry, the workflow jumps to that stage (or ends).
- `expectedStatus` is still enforced: if the response status does not equal `expectedStatus`, execution fails, even if a jump target exists.
- If `expectedStatus` is `200` and `jumpOnStatus` has `200`, the jump is taken.
- If no status matches `jumpOnStatus` and the response equals `expectedStatus`, execution continues to the next stage.
- If the response matches neither `expectedStatus` nor any `jumpOnStatus` entry, execution fails.

## endStage

```yaml
endStage:
  output:
    account: "{{stage:create-account.output.dto}}"
  context:
    tokenId: "{{stage:login.output.jwt}}"
  result:
    message: "Workflow completed successfully."
```

- `output`: workflow output (referenced by parent workflows).
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
