# Positioning and Roadmap

This document keeps the product-positioning and roadmap material out of the root `README.md` while preserving it as a first-class reference.

## Key advantages

### Modular workflow composition

Unlike monolithic API collections, workflows can reference other workflows as reusable modules.

```yaml
references:
  workflows:
    - name: "login"
      path: "./{{env:TENANT}}/login.workflow"
stages:
  - name: "authenticate"
    kind: "Workflow"
    workflowRef: "login"
```

`references.workflows[].path` can be templated, so child workflows can vary by environment or business input while keeping the same `workflowRef`.

### Contract-first validation

Validate endpoints against cached API contract specifications before execution:

```bash
sih --dry-run --verbose
```

This catches endpoint mismatches, missing parameters, and schema violations during validation, not after a live call.

### GitOps-ready workflows

YAML workflows are readable, diffable, and reviewable in Git.

```diff
+ - name: "create-org"
+   kind: "Endpoint"
+   apiRef: "accounts"
+   endpoint: "/api/organizations"
```

### Context propagation

Pass JWTs, ids, and derived values between stages and child workflows without scripting.

```yaml
endStage:
  context:
    tokenId: "{{stage:login.output.jwt}}"
    orgId: "{{stage:create-org.output.id}}"
```

### Dynamic value generation

Generate random values directly in workflow expressions:

```yaml
{{rand:number(1,25)}}
{{rand:text(10, 'alnum')}}
{{rand:datetime(system:datetime.utcnow - P30D, system:datetime.utcnow)}}
```

### Multi-version API catalog

Manage versions and environments in one catalog:

```yaml
- version: "3.11"
  definitions:
    - name: example-service
      contractType: openapi
      openApiUrl: /example/swagger/v1/swagger.json
      basePath: /ocapi
      baseUrl:
        dev: https://dev.api.com
        pre: https://pre.api.com
        prod: https://api.com
```

### CI/CD native

Workflows execute directly in pipelines:

```bash
sih \
  --workflow ./workflows/smoke-test.workflow \
  --env prod \
  --dry-run
```

### Offline-first and cloud-free

- Workflows, catalogs, and cache artifacts live on disk.
- No account or hosted control plane is required for execution.
- Telemetry is optional.
- YAML artifacts remain editable with any editor.

## When to use each tool

Use Postman, Apidog, or Bruno for:

- Interactive API exploration
- Manual debugging
- Team-facing API documentation
- Learning unfamiliar APIs

Use SphereIntegrationHub for:

- Complex multi-step orchestration
- Automated integration flows in CI/CD
- Contract-aware validation against versioned OpenAPI
- Reproducible workflows stored in Git
- Production smoke tests and health checks
- Reusable parent-child workflow composition

## Current position

SphereIntegrationHub is currently strongest as a local-first API orchestration runtime plus AI-assisted workflow authoring surface.

- Contract-aware endpoint execution with versioned OpenAPI validation
- Workflow composition and reusable child workflows
- Parent failure propagation from child workflows
- Idempotent HTTP branching with `expectedStatuses`, `onStatus`, `jumpOnStatus`, and `ensure`
- JSON-aware expressions and structured `Object` and `Array` inputs
- Response token validation against endpoint mock payloads during workflow validation
- Optional path segments with `?` for sparse JSON payloads
- `bodyFile`, `dataFile`, and `forEach` for large payloads and collection bootstraps
- Aggregated `forEach` result state via `foreach_results`, `foreach_success_count`, and `foreach_failed_count`
- JSON and HTML execution reports with stage timelines and summary output
- Interactive HTML trace report with per-stage drill-down and historical execution loading
- Versioned plugin contract with plugin-declared capabilities and non-OpenAPI `connections`
- Built-in `http`, `openai`, and `vaultwarden` plugins
- MCP server with 35 implemented tools across authoring and analysis workflows
- GitHub Action support for workflow execution in CI/CD

## Near-term priorities

Canonical backlog tracking now lives in GitHub. This document keeps local references only.

1. [`SIF-001`](https://github.com/PinedaTec-EU/SphereIntegrationHub/issues/5): add assertions and regression diagnostics on execution reports.
2. [`SIF-002`](https://github.com/PinedaTec-EU/SphereIntegrationHub/issues/6): support snapshot authoring and regression-testing workflows.
3. [`SIF-003`](https://github.com/PinedaTec-EU/SphereIntegrationHub/issues/7): package and diagnose third-party stage plugins.
4. [`SIF-004`](https://github.com/PinedaTec-EU/SphereIntegrationHub/issues/8): add transport-level retry controls beyond readiness policies.

## Mid-term roadmap

1. [`SIF-005`](https://github.com/PinedaTec-EU/SphereIntegrationHub/issues/9): add an optional web dashboard for execution history and diagnostics.
2. [`SIF-006`](https://github.com/PinedaTec-EU/SphereIntegrationHub/issues/10): build a visual workflow editor on top of the YAML runtime.
3. [`SII-001`](https://github.com/PinedaTec-EU/SphereIntegrationHub/issues/11): define higher-level runtime primitives beyond the current orchestration surface.
4. [`SIF-007`](https://github.com/PinedaTec-EU/SphereIntegrationHub/issues/12): expand secret provider support beyond Vaultwarden.

## Ongoing investment

1. [`SIT-001`](https://github.com/PinedaTec-EU/SphereIntegrationHub/issues/13): keep MCP capabilities aligned with runtime capabilities.
2. [`SIT-002`](https://github.com/PinedaTec-EU/SphereIntegrationHub/issues/14): improve authoring ergonomics, examples, repair tools, and diagnostics.
