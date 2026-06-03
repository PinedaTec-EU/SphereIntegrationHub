# MCP Authoring Quick Reference

Use this page for first-run SIH authoring with an AI assistant. It answers the common syntax questions directly and points to the deeper docs only when you need more than the happy path.

For the broader MCP overview, tool catalog, and setup flow, see [`mcp-server.md`](mcp-server.md). For deeper runtime semantics, see [`runtime-semantics.md`](runtime-semantics.md), [`variables.md`](variables.md), and [`conditional-expressions.md`](conditional-expressions.md).

## Canonical token syntax

SIH template tokens use `{{ ... }}`.

- Inputs: `{{input.username}}`
- Workflow globals: `{{global.accountId}}`
- Shared context: `{{context.tokenId}}`
- Environment variables: `{{env:API_BASE_URL}}`
- Derived vars: `{{var:subscriptionId}}`
- Endpoint response status: `{{response.status}}`
- Endpoint response body: `{{response.body}}`
- Response body path: `{{response.body.account.id}}`
- Response headers: `{{response.headers.Authorization}}`
- Stage output: `{{stage:create-account.output.accountId}}`
- Child workflow output: `{{stage:child.workflow.output.accountId}}`

## Arrays and JSON paths

Use dot-path numeric segments for arrays.

- Correct: `{{response.body.vehicles.0.id}}`
- Correct: `{{stage:create.output.items.0.id}}`
- Not supported as canonical token syntax: `{{response.body.vehicles[0].id}}`

If you need a short rule, use this one:

- Objects use dot notation.
- Arrays also use dot notation, with the numeric index as another path segment.

## Response token rules

Use `response.*` only inside `Endpoint` stages.

- `{{response.status}}` is the HTTP status code.
- `{{response.body}}` is the full response body.
- `{{response.body.id}}` reads a field from a JSON response body.
- `{{response.headers.Content-Type}}` reads a response header.

When the body is JSON, both forms are valid:

- `{{response.body.account.id}}`
- `{{response.account.id}}`

Use `response.body.*` when you want the intent to stay explicit.

## Stage token rules

Use stage outputs after you map them in `output:`.

```yaml
stages:
  - name: "create-account"
    kind: "Endpoint"
    output:
      accountId: "{{response.body.id}}"

  - name: "get-account"
    kind: "Endpoint"
    endpoint: "/api/accounts/{{stage:create-account.output.accountId}}"
```

For child workflow stages, use the workflow-qualified form:

- `{{stage:child.workflow.output.accountId}}`
- `{{stage:child.workflow.result.status}}`

## Common expression helpers

Use these in `runIf` and related expression contexts:

- `exists({{stage:create.output.accountId}})`
- `empty({{input.optionalValue}})`
- `coalesce({{stage:a.output.id}}, {{stage:b.output.id}}, 'pending')`
- `jsonLength({{response.body.items}}) > 0`
- `!isEmptyJson({{response.body}})`

Use these in template tokens:

- `{{coalesce(stage:create.output.accountId, stage:lookup.output.accountId)}}`
- `{{rand:guid()}}`
- `{{rand:number(1,25)}}`
- `{{system:date.utcnow + P7D}}`

## Safe navigation for optional values

Append `?` to the segment that may be missing.

- Optional response field: `{{response.body.account.status?}}`
- Optional stage output key: `{{stage:create.output.accountAppId?}}`
- Stage may be skipped: `{{stage:create?.output.accountId}}`

Use this when a branch may not run or when an output key is optional.

## Frequent mistakes

### `result.0.value` or `result[0].value`?

Use `result.0.value` inside SIH template tokens and expression paths.

### `env.NAME` or `env:NAME`?

Use `{{env:NAME}}`.

### `stage.name.output.id` or `stage:name.output.id`?

Use `{{stage:name.output.id}}`.

### `response.status` or `response.body.status`?

They mean different things:

- `{{response.status}}` = HTTP status code
- `{{response.body.status}}` = `status` field inside the JSON body

## When this page is not enough

Go to the deeper docs when you need:

- Full MCP setup and tool catalog: [`mcp-server.md`](mcp-server.md)
- Token/runtime semantics and `forEach`: [`runtime-semantics.md`](runtime-semantics.md)
- Variable scopes and `.wfvars`: [`variables.md`](variables.md)
- Branching, `coalesce()`, and skipped-stage patterns: [`conditional-expressions.md`](conditional-expressions.md)
