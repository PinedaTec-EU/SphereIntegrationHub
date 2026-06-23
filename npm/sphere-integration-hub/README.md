# Sphere Integration Hub

NPM distribution for the Sphere Integration Hub CLI and MCP server.

The package installs the `sih` and `sih-mcp` commands and downloads the matching
platform binaries from the GitHub release identified by `sihReleaseVersion`.

```bash
npm install -g @pinedatec.eu/sphere-integration-hub
sih --version
sih-mcp
```

Repository: https://github.com/PinedaTec-EU/SphereIntegrationHub

## What this package gives you

Sphere Integration Hub is built around four main pillars:

- **Workflow-based API orchestration**: run reproducible multi-step API workflows from Git-friendly YAML.
- **Dry-run and contract-aware validation**: validate workflows against versioned `api.catalog` definitions and cached OpenAPI contracts before live execution.
- **Execution reporting and regression tooling**: generate JSON/HTML traces, create snapshot baselines, and compare later runs against known-good executions.
- **AI-assisted authoring through MCP**: expose SIH capabilities through `sih-mcp` so coding agents can generate, inspect, and validate workflows with the same runtime rules.

Use this npm package when you want those capabilities without requiring a preinstalled .NET SDK or runtime on the target machine.

## Included commands

- `sih`: CLI for workflow validation, execution, reporting, snapshots, and report viewing.
- `sih-mcp`: MCP server for workflow authoring, catalog/cache workflows, and validation flows driven by AI tools.

## First run

Validate a workflow without calling endpoints:

```bash
sih --workflow ./workflows/create-account.workflow --env dev --dry-run --verbose
```

Execute the same workflow:

```bash
sih --workflow ./workflows/create-account.workflow --env dev
```

Start the MCP server:

```bash
sih-mcp
```

## Minimum project shape

For normal SIH usage, keep these files together:

- `*.workflow`: the main YAML workflow definition.
- `*.wfvars` (optional): input values passed to the workflow.
- `workflows.config` (optional but recommended): plugin activation, reporting defaults, secret providers, and runtime defaults.

Typical layout:

```text
./workflows.config
./workflows/create-account.workflow
./workflows/create-account.wfvars
```

Minimum `workflows.config` for normal HTTP workflows:

```yaml
plugins:
  - http
```

## Compact workflow contract

This is the minimum schema an agent or developer should assume when authoring SIH workflows from the npm package alone.

### Top-level fields

- `version` (required): catalog version to validate against.
- `id` (required): workflow ULID.
- `name` (required): workflow name.
- `description` (optional): human description.
- `output` (optional bool): whether to write `{name}.{executionId}.workflow.output`.
- `references` (optional): external API/workflow references and optional `environmentFile`.
- `input` (optional): declared workflow inputs.
- `initStage` (optional): initial globals and shared context.
- `vars` (optional): lazily evaluated derived variables addressable as `{{var:name}}`.
- `resilience` (optional): reusable retry/circuit-breaker policies.
- `stages` (required in practice): workflow stages.
- `endStage` (optional): final output, context updates, and assertions.

### Canonical token syntax

- Inputs: `{{input.username}}`
- Globals: `{{global.accountId}}`
- Context: `{{context.tokenId}}`
- Environment variables: `{{env:API_BASE_URL}}`
- Derived vars: `{{var:subscriptionId}}`
- Endpoint status: `{{response.status}}`
- Endpoint body: `{{response.body}}`
- Endpoint body field: `{{response.body.account.id}}`
- Response header: `{{response.headers.Authorization}}`
- Stage output: `{{stage:create-account.output.accountId}}`
- Child workflow output: `{{stage:child.workflow.output.accountId}}`
- Child workflow result: `{{stage:child.workflow.result.status}}`

Rules that matter:

- Use `{{ ... }}` syntax everywhere.
- Arrays use dot notation, not brackets: `{{response.body.items.0.id}}`.
- `response.*` tokens are valid only inside `Endpoint` stages.
- Use safe navigation with `?` when a segment may be missing: `{{response.body.account.status?}}`.

### Common helpers

Use these in expressions such as `runIf`:

- `exists({{stage:create.output.accountId}})`
- `empty({{input.optionalValue}})`
- `coalesce({{stage:a.output.id}}, {{stage:b.output.id}}, 'pending')`
- `jsonLength({{response.body.items}}) > 0`
- `!isEmptyJson({{response.body}})`

Use these in template values:

- `{{coalesce(stage:create.output.accountId, stage:lookup.output.accountId)}}`
- `{{rand:guid()}}`
- `{{rand:number(1,25)}}`
- `{{system:date.utcnow + P7D}}`

### Endpoint stage shape

For direct API calls, the normal happy-path stage is:

```yaml
stages:
  - name: "create-account"
    kind: "Endpoint"
    apiRef: "accounts"
    endpoint: "/api/accounts"
    httpVerb: "POST"
    expectedStatus: 201
    headers:
      Authorization: "Bearer {{env:API_TOKEN}}"
    body: |
      {
        "email": "{{input.email}}",
        "name": "{{input.name}}"
      }
    output:
      accountId: "{{response.body.id}}"
      accountStatus: "{{response.body.status?}}"
    assertions:
      - name: "account id returned"
        actual: "{{stage:create-account.output.accountId}}"
        operator: "notEmpty"
```

Common stage fields:

- `expectedStatus` or `expectedStatuses`
- `headers`, `query`, `body`, `bodyFile`
- `output`, `assertions`, `secretOutputs`
- `runIf`, `delaySeconds`
- `retry`, `circuitBreaker`
- `forEach`, `forEachSequential`, `itemName`, `indexName`, `dataFile`
- `onStatus`, `jumpOnStatus`, `ensure`

### Child workflow stage shape

Use `kind: "Workflow"` when one workflow calls another:

```yaml
references:
  workflows:
    - name: "create-account-child"
      path: "./create-account-child.workflow"

stages:
  - name: "create-account-child"
    kind: "Workflow"
    workflowRef: "create-account-child"
    inputs:
      email: "{{input.email}}"
      name: "{{input.name}}"
    output:
      accountId: "{{stage:create-account-child.workflow.output.accountId}}"
```

Important differences from endpoint stages:

- Use `workflowRef`, not `apiRef` / `endpoint` / `httpVerb`.
- Child workflow outputs are read from `{{stage:<name>.workflow.output.*}}`.
- Child workflow result metadata is read from `{{stage:<name>.workflow.result.*}}`.

### Minimal runnable workflow

```yaml
version: "1.0"
id: "01JEXAMPLE000000000000000001"
name: "create-account"
description: "Creates an account and returns its id."
references:
  apis:
    - name: "accounts"
      definition: "accounts"
input:
  - name: "email"
    type: "Text"
    required: true
  - name: "name"
    type: "Text"
    required: true
stages:
  - name: "create-account"
    kind: "Endpoint"
    apiRef: "accounts"
    endpoint: "/api/accounts"
    httpVerb: "POST"
    expectedStatus: 201
    body: |
      {
        "email": "{{input.email}}",
        "name": "{{input.name}}"
      }
    output:
      accountId: "{{response.body.id}}"
endStage:
  output:
    accountId: "{{stage:create-account.output.accountId}}"
```

Matching `.wfvars`:

```yaml
email: "ada@example.com"
name: "Ada Lovelace"
```

## Report and regression tooling

The CLI can generate JSON/HTML execution reports, create regression snapshots from known-good runs, compare later executions against those snapshots, and open an interactive report viewer with assertion diagnostics and baseline comparison.

Important distinction:

- `*.workflow.report.json` is the full execution artifact generated by a run.
- `*.workflow.snapshot.json` is a stable baseline derived from one report.
- A snapshot is not just the same JSON renamed. It removes volatile fields such as execution id, timestamps, durations, file paths, and tool version, while keeping the regression-relevant workflow/result/output/stage/assertion/preflight signals.

Typical flow:

1. Run a workflow and generate `*.workflow.report.json`.
2. Pick a known-good report and convert it into `*.workflow.snapshot.json`.
3. Compare later reports against that snapshot baseline.

```bash
sih snapshot create ./output/create-account.01J....workflow.report.json --name happy-path
sih report ./output --snapshot ./snapshots --no-open
```

## Learn more

- Repo and full docs: https://github.com/PinedaTec-EU/SphereIntegrationHub
- Getting started: https://github.com/PinedaTec-EU/SphereIntegrationHub/blob/main/.doc/getting-started.md
- MCP authoring quick reference: https://github.com/PinedaTec-EU/SphereIntegrationHub/blob/main/.doc/mcp-authoring-quick-reference.md

## If you are an AI model or coding agent

If you reached SIH through `sih-mcp`, this README should be enough for first-pass workflow authoring. Go to the repo docs when you need advanced semantics such as plugin-specific configuration, branching patterns, runtime edge cases, report internals, or deeper validation behavior:

- MCP quick reference: https://github.com/PinedaTec-EU/SphereIntegrationHub/blob/main/.doc/mcp-authoring-quick-reference.md
- MCP server guide: https://github.com/PinedaTec-EU/SphereIntegrationHub/blob/main/.doc/mcp-server.md
- Workflow schema: https://github.com/PinedaTec-EU/SphereIntegrationHub/blob/main/.doc/workflow-schema.md
- Runtime semantics: https://github.com/PinedaTec-EU/SphereIntegrationHub/blob/main/.doc/runtime-semantics.md
- Variables and context: https://github.com/PinedaTec-EU/SphereIntegrationHub/blob/main/.doc/variables.md
- Dry-run behavior: https://github.com/PinedaTec-EU/SphereIntegrationHub/blob/main/.doc/dry-run.md

Those documents remain the full source of truth for the runtime contract, validation semantics, and troubleshooting paths.

## If you are a developer

If you installed the npm package to use SIH in a project or pipeline, the repo is where the full product contract lives:

- Start here for onboarding: https://github.com/PinedaTec-EU/SphereIntegrationHub/blob/main/.doc/getting-started.md
- Understand workflow authoring: https://github.com/PinedaTec-EU/SphereIntegrationHub/blob/main/.doc/workflow-schema.md
- Understand catalog and contract validation: https://github.com/PinedaTec-EU/SphereIntegrationHub/blob/main/.doc/swagger-catalog.md
- Understand reports and baselines: https://github.com/PinedaTec-EU/SphereIntegrationHub/blob/main/.doc/execution-reporting.md
- Understand plugins and integrations: https://github.com/PinedaTec-EU/SphereIntegrationHub/blob/main/.doc/plugins.md
- Browse runnable samples: https://github.com/PinedaTec-EU/SphereIntegrationHub/tree/main/samples

This npm package is the delivery vehicle. The repository documentation explains how to model workflows, structure catalogs, interpret dry-run results, and integrate SIH into CI/CD or AI-assisted authoring flows.
