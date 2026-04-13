# Conditional Expressions and Skipped-Stage Output Resolution

This document covers how SIH evaluates `runIf` conditions, resolves template tokens from stages
that may have been skipped, and the three mechanisms available to write robust workflows with
mutually-exclusive branches.

---

## Background: the skipped-stage problem

A stage with a `runIf` condition may not execute at runtime. If a subsequent stage references the
output of a skipped stage — in a template string or in a `runIf` expression — the behavior
depended on the context:

| Context | Old behaviour | New behaviour (≥ 1.7) |
|---------|--------------|----------------------|
| `runIf` expression | Token resolved to `Missing` (null-like) — already safe | Same |
| Template string (`value:`, `body:`, `output:`, …) | **Threw an exception** — workflow failed | Resolves to `""` (empty) |

The root cause: the expression evaluator already silenced "not found" errors into `Value.Missing`,
but `TemplateResolver.ResolveTemplate` had no equivalent guard for skipped stages.

---

## Three mechanisms

### 1. Automatic null for skipped stages

When a stage is skipped because its `runIf` evaluated to `false`, SIH registers it internally as
**skipped**. Any subsequent template token that references a skipped stage's output resolves to
`""` instead of throwing.

```yaml
stages:
  - name: "create-subscription"
    kind: "Endpoint"
    runIf: "{{input.tenantId}} == null"
    # ...
    output:
      subscriptionId: "{{response.body.id}}"

  - name: "activate"
    kind: "Endpoint"
    # Works even if create-subscription was skipped:
    # subscriptionId will be "" when skipped — add a guard in runIf if needed
    body: |
      { "subscriptionId": "{{stage:create-subscription.output.subscriptionId}}" }
```

**This is a passive improvement** — no workflow changes required. Existing workflows that were
failing due to skipped-stage references will now recover with an empty string.

> **Important:** an empty string is not the same as a valid ID. If the downstream stage requires
> the value, guard it with a `runIf` check (see examples below) or use `coalesce()`.

---

### 2. `coalesce()` in template strings

`coalesce(arg1, arg2, ...)` returns the first non-empty argument. It was already available in
`runIf` expressions; it is now also available inside `{{ }}` template tokens.

**Syntax:** `{{coalesce(token1, token2, ...)}}`

Each argument is a bare token path (no surrounding `{{ }}`).

```yaml
endStage:
  output:
    # Returns the ID from whichever branch actually ran
    subscriptionId: >-
      {{coalesce(
        stage:create-subscription.output.subscriptionId,
        stage:create-subscription-with-tenant.output.subscriptionId
      )}}
```

Behaviour:
- Returns the first argument that resolves to a non-empty string.
- Silently skips arguments that throw "not found" errors (missing stages, missing keys).
- Returns `""` if all arguments are missing or empty.

`coalesce()` arguments are evaluated **lazily**: resolution stops at the first non-empty match.

---

### 3. Safe navigation with `?`

Append `?` to a stage name or an output key to suppress "not found" errors for that specific
position.

| Syntax | Effect |
|--------|--------|
| `{{stage:name?.output.key}}` | Returns `""` if stage `name` was never registered (skipped or branch not taken) |
| `{{stage:name.output.key?}}` | Returns `""` if the stage ran but key `key` is absent in its output |
| `{{stage:name?.output.key?}}` | Both guards active |

```yaml
# Stage ran but the key might not exist (e.g., 409 path vs. 201 path):
accountAppId: "{{stage:create-account.output.accountAppId?}}"

# Stage may not have run at all (conditional branch):
subscriptionId: "{{stage:create-subscription?.output.subscriptionId}}"
```

Safe navigation works in **both template strings and `runIf` expressions**:

```yaml
runIf: >-
  {{stage:create-subscription?.output.subscriptionId}} != null ||
  {{stage:create-subscription-with-tenant?.output.subscriptionId}} != null
```

---

## Mutually-exclusive branches

The canonical pattern for exclusive branches is to combine all three mechanisms:

```yaml
stages:
  - name: "create-subscription"
    kind: "Endpoint"
    runIf: "{{input.tenantId}} == null"
    output:
      subscriptionId: "{{response.body.id}}"

  - name: "create-subscription-with-tenant"
    kind: "Endpoint"
    runIf: "{{input.tenantId}} != null"
    output:
      subscriptionId: "{{response.body.id}}"

  - name: "activate-subscription"
    kind: "Endpoint"
    # Guard: at least one branch must have produced an ID
    runIf: >-
      {{stage:create-subscription?.output.subscriptionId}} != null ||
      {{stage:create-subscription-with-tenant?.output.subscriptionId}} != null
    body: |
      {
        "subscriptionId": "{{coalesce(
          stage:create-subscription.output.subscriptionId,
          stage:create-subscription-with-tenant.output.subscriptionId
        )}}"
      }

endStage:
  output:
    subscriptionId: >-
      {{coalesce(
        stage:create-subscription.output.subscriptionId,
        stage:create-subscription-with-tenant.output.subscriptionId
      )}}
```

See [`samples/sample-exclusive-branches.workflow`](../samples/sample-exclusive-branches.workflow)
for a complete, runnable example.

---

## Decision guide

| Situation | Recommended approach |
|-----------|---------------------|
| Downstream stage needs the value — should be skipped if the source was | Add `runIf` guard using `?` safe nav or `coalesce() != ''` |
| Two branches produce the same logical value | `coalesce()` in output/body templates |
| A stage ran but a specific output key may be absent (e.g., on a 409 path) | `key?` safe nav on the output key |
| A stage may not have run at all | `stage:name?` safe nav on the stage name, or rely on Mejora 1 silencing |
| `runIf` of later stages depends on exclusive branch success | `coalesce(...) != ''` or `name?.output.key != null` |

---

## Mejora 4 — `vars:` block (derived variables)

Declare named derived variables at the workflow level. Each var is a template string evaluated
**lazily** at the point of reference using the live execution context (stage outputs already
available, globals, inputs, etc.).

```yaml
vars:
  subscriptionId: >-
    {{coalesce(
      stage:create-subscription.output.subscriptionId,
      stage:create-subscription-with-tenant.output.subscriptionId
    )}}
  activationUrl: "/api/subscriptions/{{var:subscriptionId}}/activate"
```

### Token syntax

`{{var:name}}` — resolves the var template and returns the result.

Vars support JSON path navigation: `{{var:myObject.nested.field}}` — the var template must
resolve to valid JSON.

### Why this matters

Without `vars:`, a `coalesce()` expression over two exclusive branches must be copy-pasted into
every stage that needs the value (`body:`, `runIf:`, `endStage.output:`). With `vars:`, it is
declared once and referenced everywhere.

```yaml
# Before
body: |
  { "id": "{{coalesce(stage:create-subscription.output.id, stage:create-subscription-with-tenant.output.id)}}" }

# After
vars:
  subscriptionId: "{{coalesce(stage:create-subscription.output.id, stage:create-subscription-with-tenant.output.id)}}"

body: |
  { "id": "{{var:subscriptionId}}" }
```

### Scope

- `vars:` are scoped to the workflow that declares them. Nested workflow stages do not inherit the
  parent's vars, but they can define their own.
- Iteration contexts (`forEach`) do inherit the parent vars.
- Self-referential vars will cause a stack overflow — the DSL does not detect cycles.

---

## Mejora 5 — `onSkip.output` block

Declare outputs to register when a stage is skipped. The values are template strings resolved at
**skip time** using the execution context (outputs from prior stages are available).

```yaml
- name: "create-subscription"
  kind: "Endpoint"
  runIf: "{{input.tenantId}} == null"
  output:
    subscriptionId: "{{response.body.id}}"
  onSkip:
    output:
      subscriptionId: "{{stage:create-subscription-with-tenant.output.subscriptionId}}"
```

### Why this matters

Without `onSkip.output`, downstream stages must use `coalesce()` or `?` safe navigation to handle
the case where a branch was not taken. With `onSkip.output`, both branches expose the **same key
name** — the downstream stage sees a consistent interface regardless of which branch ran.

```yaml
# Without onSkip — downstream must know about both branches
body: |
  { "id": "{{coalesce(stage:create-subscription.output.subscriptionId, stage:create-subscription-with-tenant.output.subscriptionId)}}" }

# With onSkip on both branches
body: |
  { "id": "{{stage:create-subscription.output.subscriptionId}}" }
  # OR
  { "id": "{{stage:create-subscription-with-tenant.output.subscriptionId}}" }
  # Both are always available — one carries the real value, the other mirrors it
```

### Semantics

- The stage still appears as "Skipped" in the execution report.
- `onSkip.output` values are registered in the output context, so `SkippedStages` resolution
  checks the output dictionary first: if a key is found, it is returned; otherwise, empty string.
- The templates in `onSkip.output` can reference any previously resolved token (inputs, globals,
  context, other stage outputs).

### Combined pattern with `vars:`

`onSkip.output` and `vars:` complement each other:

- Use `onSkip.output` when the two branches are symmetric and you want each stage to advertise the
  same key name.
- Use `vars:` when the downstream code is asymmetric and you want a single canonical alias.

---

## Known limitations

- **`coalesce()` in template strings** is positional: arguments are bare token paths, not full
  expressions. Complex expressions inside `coalesce()` arguments are not supported in templates
  (use `runIf` expressions for those).

- **Workflow-stage safe navigation** (`stage:name?.workflow.output.key`) is supported but errors
  inside `TryResolveStageWorkflowOutput` are caught and silenced, which may hide legitimate
  mis-configurations. If a workflow stage ran but returned unexpected keys, add an explicit
  `runIf` guard instead of relying solely on `?`.

- **Variable derivation** (`vars:` block at workflow level) and **alias stages** (steps that
  compose outputs without calling an endpoint) are not yet implemented. For now, use `coalesce()`
  in `endStage.output` to unify branches.
