<p align="center">
  <a href="https://github.com/PinedaTec-EU/SphereIntegrationHub">
    <img loading="lazy" alt="Sphere Integration Hub" src="./.doc/SIH.png" width="85%"/>
  </a>
</p>

[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/PinedaTec-EU/SphereIntegrationHub)
[![License MIT](https://img.shields.io/badge/MIT_license-blue)](https://opensource.org/licenses/MIT)
[![npm](https://img.shields.io/npm/v/@pinedatec.eu/sphere-integration-hub?label=npm)](https://www.npmjs.com/package/@pinedatec.eu/sphere-integration-hub)
[![npm downloads](https://img.shields.io/npm/dm/@pinedatec.eu/sphere-integration-hub)](https://www.npmjs.com/package/@pinedatec.eu/sphere-integration-hub)
![MCP](https://img.shields.io/badge/MCP-35_tools-purple)
[![NuGet Version](https://img.shields.io/nuget/v/SphereIntegrationHub.Tool.svg?label=NuGet+CLI)](https://www.nuget.org/packages/SphereIntegrationHub.Tool/)
[![NuGet Version](https://img.shields.io/nuget/v/SphereIntegrationHub.MCP.Tool.svg?label=NuGet+MCP)](https://www.nuget.org/packages/SphereIntegrationHub.Mcp.Tool/)
[![GitHub Release](https://img.shields.io/github/v/release/PinedaTec-EU/SphereIntegrationHub?label=release)](https://github.com/PinedaTec-EU/SphereIntegrationHub/releases)
[![GitHub commits](https://img.shields.io/github/commit-activity/m/PinedaTec-EU/SphereIntegrationHub)](https://github.com/PinedaTec-EU/SphereIntegrationHub/commits/main)
[![GitHub Issues](https://img.shields.io/github/issues/PinedaTec-EU/SphereIntegrationHub)](https://github.com/PinedaTec-EU/SphereIntegrationHub/issues)
[![GitHub Stars](https://img.shields.io/github/stars/PinedaTec-EU/SphereIntegrationHub?style=social)](https://github.com/PinedaTec-EU/SphereIntegrationHub/stargazers)
[![Twitter Follow](https://img.shields.io/twitter/follow/jmrpineda?style=social)](https://twitter.com/jmrpineda)
[![LinkedIn](https://img.shields.io/badge/LinkedIn-Connect-blue?logo=linkedin)](https://www.linkedin.com/in/jmrpineda)

<p align="center">
  <img src="./.doc/icon.svg" width="90" height="90" alt="SphereIntegrationHub icon"/>
</p>

CLI tool to orchestrate API calls using versioned Swagger catalogs and YAML workflows. Workflows can reference other workflows, share context (like JWTs), validate endpoints against cached Swagger specs, and run in dry-run mode for validation.

Stage execution is being opened through a versioned plugin contract: workflow orchestration stays in the runtime, while protocol/channel implementations such as HTTP can live in dedicated plugins.

Documentation:

- [`Overview`](.doc/overview.md)
- [`Why SphereIntegrationHub`](.doc/why-sih.md)
- [`workflow schema`](.doc/workflow-schema.md)
- [`swagger catalog`](.doc/swagger-catalog.md)
- [`cli help`](.doc/cli.md)
- [`variables and context`](.doc/variables.md)
- [`dry-run validation`](.doc/dry-run.md)
- [`execution reporting`](.doc/execution-reporting.md)
- [`open telemetry`](.doc/telemetry.md)
- [`MCP Server`](.doc/mcp-server.md) - AI-assisted workflow creation (35 tools, all levels)
- [`GitHub Action`](.doc/github-action.md) - run workflows from any CI/CD pipeline
- [`plugins`](.doc/plugins.md)
- [`secret providers`](.doc/secret-providers.md)

Examples:

- [`samples/sample-bootstrap.workflow`](samples/sample-bootstrap.workflow) uses the explicit `Http` plugin stage with a plugin-specific `config` block.
- [`samples/workflows.config`](samples/workflows.config) shows explicit plugin activation. If `plugins` is omitted, the runtime still enables `http` for compatibility but emits a warning; a future release will require the section.
- [`samples/api.catalog`](samples/api.catalog) shows plugin declaration/version binding in the catalog.
- [`samples/vaultwarden-secrets`](samples/vaultwarden-secrets) shows the `vaultwarden` secret provider feeding `{{env:...}}` tokens. Secret provider failures are fail-fast and abort the run before workflow loading continues.

## Community

If you use SphereIntegrationHub in your company or project, we'd love to hear about it!

- Give us a ⭐ on GitHub — it helps the project grow
- Share your experience on [LinkedIn](https://www.linkedin.com/in/jmrpineda) mentioning **#SphereIntegrationHub** — we repost and feature use cases
- Drop us a line at [sih@pinedatec.eu](mailto:sih@pinedatec.eu) — tell us what you're automating, we'd love to feature it

## Installation

### npm / npx — no .NET required

The fastest way to get started. No runtime dependencies, no SDK to install.

**Global install** (run `sih` and `sih-mcp` anywhere):

```bash
npm install -g @pinedatec.eu/sphere-integration-hub
sih --version
```

**One-off via npx** (MCP server, ideal for Claude Desktop / VS Code):

```bash
npx @pinedatec.eu/sphere-integration-hub          # launches the MCP server
npx -p @pinedatec.eu/sphere-integration-hub sih --version   # CLI one-off
```

**For teams and CI** (pin to repo):

```bash
npm install --save-dev @pinedatec.eu/sphere-integration-hub
# teammates and CI: npm install picks it up automatically
```

### dotnet tool — for .NET developers

If you already have .NET 10+ installed, the tool is also available on NuGet:

```bash
dotnet tool install -g SphereIntegrationHub.Tool        # CLI
dotnet tool install -g SphereIntegrationHub.Mcp.Tool   # MCP server
```

Or as a local tool (recommended for teams using dotnet):

```bash
dotnet new tool-manifest   # only if .config/dotnet-tools.json doesn't exist yet
dotnet tool install SphereIntegrationHub.Tool
dotnet tool restore        # teammates and CI run this to pick it up
```

NuGet packages: [CLI](https://www.nuget.org/packages/SphereIntegrationHub.Tool/) · [MCP](https://www.nuget.org/packages/SphereIntegrationHub.Mcp.Tool/)

## Catalog

The API catalog is a fixed file with versions and API definitions. `api.catalog` is the preferred name and is stored as YAML, though existing legacy JSON/YAML catalog files are still supported during the transition. Each definition provides its own environment base URLs and a relative swagger path. An optional `healthCheck` probes the API before swagger caching and workflow execution, and an optional `readiness` policy controls retries/timeouts and accepted HTTP status codes:

`src/resources/api.catalog`

If your repository already has a previous catalog file, migrate it to `api.catalog` instead of keeping both names in parallel.

Compatibility policy:

- Legacy JSON catalog files remain supported through the `1.7` line for backward compatibility.
- Starting in `1.8`, the recommended migration path is the MCP tool `migrate_api_catalog`.
- Legacy JSON support is transitional and will be removed after that compatibility window, so new catalog authoring should target `api.catalog` directly.

Swagger definitions are cached per version in:

`src/resources/cache/{version}/{definition}.json`

When the CLI resolves default paths from a workflow location, it treats the nearest ancestor `workflows/` folder as shared root. That keeps `api.catalog` and `cache/` centralized even if workflows are organized in nested subfolders under `workflows/`.

## GitHub Action

The `run-sphere-workflow` composite action lets you install the SphereIntegrationHub CLI and execute a workflow from any GitHub Actions pipeline with a single step.

```yaml
- uses: PinedaTec-EU/SphereIntegrationHub/.github/actions/run-sphere-workflow@main
  with:
    workflow-path: ./workflows/create-account.workflow
    cli-args: --env prod --catalog ./api.catalog
```

The action always runs `dotnet tool restore` first (so other tools in your manifest are not affected) and then updates `SphereIntegrationHub.Tool` — to the latest version by default, or to the exact version you specify via `tool-version`.

**Pre-deploy smoke test:**

```yaml
- uses: PinedaTec-EU/SphereIntegrationHub/.github/actions/run-sphere-workflow@main
  with:
    workflow-path: ./workflows/smoke-test.workflow
    cli-args: --env prod --dry-run --verbose
```

**Pin to a fixed version for reproducible production pipelines:**

```yaml
- uses: PinedaTec-EU/SphereIntegrationHub/.github/actions/run-sphere-workflow@main
  with:
    workflow-path: ./workflows/onboard-customer.workflow
    tool-version: '1.5.12.149'
    cli-args: --env prod --catalog ./api.catalog --varsfile ./prod.wfvars
```

**Upload execution report as a CI artifact:**

```yaml
- uses: PinedaTec-EU/SphereIntegrationHub/.github/actions/run-sphere-workflow@main
  with:
    workflow-path: ./workflows/integration-test.workflow
    cli-args: --env pre --report-format both --capture-http bodies

- uses: actions/upload-artifact@v4
  if: always()
  with:
    name: sih-execution-report
    path: '*.workflow.report.*'
```

See [`.doc/github-action.md`](.doc/github-action.md) for the full reference and more examples.

## Workflow overview

SphereIntegrationHub workflows are plain YAML and are designed to stay readable even when orchestration gets complex.

- Reference APIs from the versioned catalog and compose parent/child workflows.
- Accept typed inputs, `.wfvars`, and `.env` values.
- Mix endpoint calls, workflow stages, retries, circuit breakers, delays, and conditional branches.
- Work with structured JSON, file-backed payloads, `forEach`, and idempotent `ensure` flows.
- Generate inline fake data with `{{rand:*}}` helpers for numbers, text, dates, datetimes, times, GUIDs, and ULIDs.
- Emit JSON and HTML execution reports for post-run diagnosis.

See [`.doc/workflow-schema.md`](.doc/workflow-schema.md) for the full schema, [`.doc/variables.md`](.doc/variables.md) for variable resolution, and [`.doc/execution-reporting.md`](.doc/execution-reporting.md) for report artifacts and viewer behavior.

## Token and Workflow Semantics

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

## Execution reporting

SphereIntegrationHub can persist each run as JSON + HTML execution artifacts for diagnosis, auditability, and sharing. The HTML viewer gives you a timeline, per-stage drill-down, and multi-run switching from the same page.

- Machine-readable JSON report
- Self-contained HTML trace viewer
- Configurable HTTP capture with redaction by default
- Stage detail includes the `forEach` execution mode (`Parallel` or `Sequential`) when iteration is enabled

See [`.doc/execution-reporting.md`](.doc/execution-reporting.md) for full usage, artifact formats, and reporting configuration.

### Example report

Interactive timeline with nested workflows, stage duration, and skipped branches:

![Execution report timeline overview](./.doc/Screenshots/execution-report-timeline-overview.png)

Execution switcher across multiple runs in the same output folder:

![Execution report run selector dropdown](./.doc/Screenshots/execution-report-run-selector-dropdown.png)

Stage drill-down with execution metadata and resolved workflow inputs/results:

![Execution report stage details](./.doc/Screenshots/execution-report-stage-details.png)

### Example workflow (forEach + fake data)

See [`samples/fake-usage-seed.workflow`](samples/fake-usage-seed.workflow) for a full example that:

- searches customers,
- iterates the returned collection with `forEach`,
- and posts fake usage events using `{{rand:*}}`.

### Example workflow (login)

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

`output: false` only disables the `.workflow.output` file written to disk for that workflow execution. It does not suppress `endStage.output`, in-memory workflow outputs, or values exposed to parent workflows through `{{workflow:<stage>.output.*}}`.

### Example workflow (idempotent bootstrap with files and foreach)

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

`forEach` runs in parallel by default. Set `forEachSequential: true` on the stage when iterations must execute one by one.

Parallel default example:

```yaml
stages:
  - name: "seed-accounts"
    kind: "Endpoint"
    apiRef: "accounts"
    endpoint: "/api/accounts"
    httpVerb: "POST"
    expectedStatus: 201
    forEach: "{{input.seed}}"
    itemName: "item"
    body: |
      {
        "id": "{{context:item.id}}",
        "name": "{{context:item.name}}"
      }
```

Sequential example when order matters or iterations depend on shared state:

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

Example `./payloads/create-account.json`:

```json
{
  "id": "{{context:item.id}}",
  "name": "{{context:item.name}}"
}
```

## Sample Workflows

The repository includes reference workflows under `samples/`:

- `sample-parent.workflow` and `sample-child.workflow`: parent/child composition, workflow outputs/results, retries, circuit breakers, and conditional follow-up stages, including a lookup that accepts `404` without retrying it.
- `sample-conditional.workflow`: compound `runIf` with `&&`, `||`, parentheses, safe missing-token checks, optional JSON paths, `empty(...)`, `coalesce(...)`, and branching from a lookup that may return `404`.
- `sample-bootstrap.workflow`: `expectedStatuses`, `onStatus`, `ensure`, `bodyFile`, `dataFile`, and `forEach` for seed/bootstrap scenarios.
- `sample-lookup-no-retry.workflow`: focused example for GET/lookup stages where `404` is a business result, not a retriable transport failure.
- `sample-parent.wfvars`: companion input example for the parent/child sample.
- `payloads/bootstrap-account.json` and `seed/accounts.json`: file-backed request and collection samples used by `sample-bootstrap.workflow`.
- `api.catalog`: reference catalog for the sample workflows — defines the `accounts` API with per-environment base URLs, `healthCheck`, and `readiness` policy for strict preflight.

Use these files directly when authoring new workflows or when prompting MCP-based generation.

## Usage

Dry-run (validates workflow, references, and swagger paths without calling endpoints):

```bash
sih \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --dry-run \
  --verbose
```

Run with mocks (uses `stage.mock` when defined):

```bash
sih \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --mocked
```

Override root `.env` for `{{env:NAME}}` tokens:

```bash
sih \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --envfile ./workflows/.env
```

Force refresh of swagger cache:

```bash
sih \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --refresh-cache
```

Execute a workflow:

```bash
sih \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --input username=user \
  --input password=secret \
  --input accountName=Acme
```

Generate full execution artifacts:

```bash
sih \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --report-format both \
  --capture-http bodies
```

This writes:

- `{name}.{executionId}.workflow.output`
- `{name}.{executionId}.workflow.report.json`
- `{name}.{executionId}.workflow.report.html`

Execution artifacts are written to the sibling `output/` directory next to the executed workflow file.

Reports include stage timings, retries, jumps, ensure status, HTTP status, redacted headers/body capture, and final outputs.

Override root `.env` for `{{env:NAME}}` tokens:

```bash
sih \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --envfile ./workflows/.env
```

### Key Advantages of SphereIntegrationHub

#### 🎯 1. Modular Workflow Composition

Unlike Postman's monolithic collections, workflows can reference other workflows as reusable modules:

```yaml
references:
  workflows:
    - name: "login"
      path: "./{{env:TENANT}}/login.workflow"
stages:
  - name: "authenticate"
    kind: "Workflow"
    workflowRef: "login"  # Reuse login workflow
```

`references.workflows[].path` can be templated, so child workflows can vary by environment or business input while keeping the same `workflowRef`.
The same applies to `references.environmentFile`, `bodyFile`, `dataFile`, and `mock.payloadFile` when those paths need to vary by tenant or environment.
`.env` values can also be chained with `{{env:NAME}}`, so shared base paths can be centralized once and reused by other environment variables.
Execution resolves child workflow paths right before a workflow stage runs. Validation and `--dry-run` may inspect them earlier; if a path still depends on business values, `--dry-run` reports a warning and leaves the final resolution to runtime, where failures are reported with the original path expression and stage context.

#### 🛡️ 2. Contract-First Validation

Validate endpoints against cached Swagger specifications **before execution**:

```bash
--dry-run --verbose  # Validates without making HTTP calls
```

This catches endpoint mismatches, missing parameters, and schema violations at validation time, not runtime.

#### 📦 3. GitOps-Ready Workflows

YAML workflows are human-readable and Git-friendly. Pull requests show exact changes:

```diff
+ - name: "create-org"
+   kind: "Endpoint"
+   apiRef: "accounts"
+   endpoint: "/api/organizations"
```

Compare this to Postman's JSON exports with GUIDs and nested structures.

#### 🔄 4. Context Propagation

Seamlessly pass JWTs, IDs, and data between workflow stages and nested workflows:

```yaml
endStage:
  context:
    tokenId: "{{stage:login.output.jwt}}"
    orgId: "{{stage:create-org.output.id}}"
```

No scripting required—context flows declaratively.

#### 🎲 5. Dynamic Value Service

Generate random values with built-in formatting:

```yaml
variables:
  - name: "accountId"
    type: "Random"
    randomType: "Guid"
  - name: "timestamp"
    type: "Random"
    randomType: "DateTime"
```

#### 🔍 6. Multi-Version API Catalog

Manage multiple API versions and environments in a single catalog:

```json
{
  "version": "3.11",
  "definitions": [
    {
      "name": "example-service",
      "swaggerUrl": "/example/swagger/v1.0/swagger.json",
      "basePath": "/ocapi",
      "baseUrl": {
        "dev": "https://dev.api.com",
        "pre": "https://pre.api.com",
        "prod": "https://api.com"
      }
    }
  ]
}
```

Swagger definitions are cached per version, ensuring validation against the correct contract.

#### 🚀 7. CI/CD Native

No conversion needed—workflows execute directly in pipelines:

```bash
sih \
  --workflow ./workflows/smoke-test.workflow \
  --env prod \
  --dry-run  # Gate deployments with validation
```

#### 💾 8. Offline-First & Cloud-Free

No account required. No cloud dependency. No internet connection needed for execution:

- **All-in-one-place**: Workflows, catalogs, and Swagger cache live on disk
- **No vendor lock-in**: No subscription, no API limits
- **Optional telemetry**: OpenTelemetry is supported but disabled by default
- **Edit anywhere**: YAML files editable with any text editor (VS Code, vim, nano)
- **Complete privacy**: Your API workflows never leave your infrastructure
- **Zero latency**: No cloud round-trips—everything runs locally

Unlike Postman (cloud sync required) or Apidog (account-based), SphereIntegrationHub shares Bruno's philosophy of local-first tooling, but adds enterprise orchestration capabilities.

### When to Use Each Tool

**Use Postman/Apidog/Bruno for:**

- Interactive API exploration and debugging
- Collaborative documentation with teams
- Manual testing during development
- Learning new APIs

**Use SphereIntegrationHub for:**

- Complex multi-step orchestration (10+ sequential calls)
- Automated integration testing in CI/CD
- Reproducible API workflows in Git
- Contract validation against versioned Swagger specs
- Production smoke tests and health checks
- Scenarios requiring workflow composition and reuse

### Roadmap

### Current Position

SphereIntegrationHub is now strong as a local-first API orchestration runtime and AI-assisted workflow authoring tool.

- ✅ Contract-aware endpoint execution with versioned Swagger validation
- ✅ Workflow composition and reusable child workflows
- ✅ Child workflow failures propagate to the parent workflow
- ✅ Idempotent HTTP branching with `expectedStatuses`, `onStatus`, `jumpOnStatus`, and `ensure`
- ✅ JSON-aware expressions and structured `Object` / `Array` inputs
- ✅ Response token validation against endpoint mock payloads during workflow validation
- ✅ Optional path segments with `?` for sparse JSON payloads
- ✅ `bodyFile`, `dataFile`, and `forEach` for large payloads and collection bootstraps
- ✅ Aggregated `forEach` workflow result state via `foreach_results`, `foreach_success_count`, and `foreach_failed_count`
- ✅ Post-execution observability with JSON/HTML reports, stage timelines, and summary output
- ✅ Interactive HTML trace report (`sih report`) — Jaeger-style timeline with per-stage drill-down, HTTP details, and file picker to load prior executions; generated as a standalone command from any `.workflow.report.json` artifact
- ✅ MCP server with 35 implemented tools across all capability levels — catalog exploration, workflow validation, stage generation, variable analysis, semantic dependency inference, pattern detection, full system synthesis, and optimization
- ✅ GitHub Action (`run-sphere-workflow`) for executing workflows from any CI/CD pipeline, with optional version pinning

### Near-Term Priorities

1. **Assertions and Regression Diagnostics**
   First-class assertions, golden snapshots, and failure diffs on top of the existing execution reports.
2. **Snapshot and Regression Testing**
   Snapshot authoring helpers and update workflows for intentional baseline changes.
3. **Documentation and Surface Alignment**
   Keep runtime, CLI, MCP, GitHub Action, and examples synchronized so the documented contract matches the implemented one.
4. **Plugin/Transformer Extensibility**
   Load custom .NET transformations and stage extensions safely.
5. **Transport-Level Retry Controls**
   Keep catalog-driven readiness strict at preflight time, and evaluate future per-stage boolean controls to opt workflow endpoint calls into the same transport retry policy when that surface is stable.

### Mid-Term Roadmap

1. **Full Web Dashboard**
   Optional persistent web interface with execution history across runs, search, filtering, and team-level diagnostics. The per-run interactive HTML trace report (`sih report`) covers local and CI diagnostics; this item targets a hosted, multi-run experience.
2. **Visual Workflow Editor**
   Web-based workflow builder for teams that want graphical authoring on top of the YAML runtime.
3. **Higher-Level Runtime Primitives**
   More semantic stage sugar beyond `ensure`, plus better assertions and reusable payload/template blocks.
4. **Broader Secret Provider Coverage**
   After the initial Vaultwarden provider, extend the same secret-provider contract to other backends such as AWS Secrets Manager, Azure Key Vault, and HashiCorp Vault.

### Ongoing Investment

1. **MCP Integration**
   Keep the MCP aligned with runtime capabilities so AI agents can generate valid workflows without inventing unsupported schema.
2. **Authoring Ergonomics**
   Improve generated examples, repair tools, diagnostics, and workflow scaffolding quality.
