# SphereIntegrationHub

CLI tool to orchestrate API calls using versioned Swagger catalogs and YAML workflows. Workflows can reference other workflows, share context (like JWTs), validate endpoints against cached Swagger specs, and run in dry-run mode for validation.

Documentation:

- [`Overview`](.doc/overview.md)
- [`Why SphereIntegrationHub`](.doc/why-sih.md)
- [`workflow schema`](.doc/workflow-schema.md)
- [`swagger catalog`](.doc/swagger-catalog.md)
- [`cli help`](.doc/cli.md)
- [`variables and context`](.doc/variables.md)
- [`dry-run validation`](.doc/dry-run.md)
- [`open telemetry`](.doc/telemetry.md)
- [`plugins`](.doc/plugins.md)

## Catalog

The API catalog is a fixed JSON file with versions, environment base URLs, and optional per-definition overrides:

`src/resources/api-catalog.json`

Swagger definitions are cached per version in:

`src/resources/cache/{version}/{definition}.json`

## Workflow overview

Workflows are YAML files with main sections:

- `version`, `id`, `name`, `description`
- `references` for workflows and API definitions
- `input` for required variables
- `initStage` for workflow-specific variables or context defaults
- `stages` (plugin-driven calls)
- `endStage` for workflow output and context updates

Templates support `{{env:NAME}}` to read environment variables anywhere values are resolved.
Use `references.environmentFile` (or CLI `--envfile`) to load a `.env` file for those variables.
Inputs from `.wfvars` can be scoped by environment and version (see `variables and context`).
Stages can declare `delaySeconds` (0-60) to delay execution. Retry and circuit breaker settings apply only to `endpoint`/`http` stages.

## Workflow plugins

Plugins are enabled via `workflows.config` next to your workflow files. The core requires at least one plugin besides the built-in `workflow` plugin.

`workflows.config` example:

```yaml
features:
  openTelemetry: true
openTelemetry:
  serviceName: "SphereIntegrationHub.cli"
  endpoint: "http://localhost:4317"
  consoleExporter: false
  debugConsole: false
plugins:
  - http
```

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
    kind: "http"
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

## Usage

Dry-run (validates workflow, references, and swagger paths without calling endpoints):

```bash
SphereIntegrationHub.cli \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --dry-run \
  --verbose
```

Run with mocks (uses `stage.mock` when defined):

```bash
SphereIntegrationHub.cli \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --mocked
```

Override root `.env` for `{{env:NAME}}` tokens:

```bash
SphereIntegrationHub.cli \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --envfile ./workflows/.env
```

Force refresh of swagger cache:

```bash
SphereIntegrationHub.cli \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --refresh-cache
```

Execute a workflow:

```bash
SphereIntegrationHub.cli \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --input username=user \
  --input password=secret \
  --input accountName=Acme
```

Override root `.env` for `{{env:NAME}}` tokens:

```bash
SphereIntegrationHub.cli \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --envfile ./workflows/.env
```

## Testing

Run unit tests:

```bash
dotnet test test/SphereIntegrationHub.Tests/SphereIntegrationHub.Tests.csproj
```

Run with coverage (line coverage threshold 85%):

```bash
dotnet test test/SphereIntegrationHub.Tests/SphereIntegrationHub.Tests.csproj \
  /p:CollectCoverage=true \
  /p:CoverletOutputFormat=cobertura \
  /p:Threshold=85 \
  /p:ThresholdType=line
```

### Key Advantages of SphereIntegrationHub

#### 🎯 1. Modular Workflow Composition
Unlike Postman's monolithic collections, workflows can reference other workflows as reusable modules:

```yaml
references:
  workflows:
    - name: "login"
      path: "./login.workflow"
stages:
  - name: "authenticate"
    kind: "workflow"
    workflowRef: "login"  # Reuse login workflow
```

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
  "baseUrl": {
    "dev": "https://dev.api.com",
    "pre": "https://pre.api.com",
    "prod": "https://api.com"
  },
  "definitions": [
    {
      "name": "example-service",
      "swaggerUrl": "/example/swagger/v1.0/swagger.json",
      "basePath": "/ocapi"
    }
  ]
}
```

Swagger definitions are cached per version, ensuring validation against the correct contract.

#### 🚀 7. CI/CD Native
No conversion needed—workflows execute directly in pipelines:

```bash
SphereIntegrationHub.cli \
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

### Future Enhancements

The following features are planned for future releases:

1. **Visual Workflow Editor** - Web-based drag-and-drop workflow builder (n8n-style) for designing complex orchestrations visually
2. **GUI/Dashboard** - Optional web interface for visualizing workflow executions and results
3. **HTML/JSON Reports** - Structured output with metrics, timings, and navigable logs
5. **Secret Manager Integration** - AWS Secrets Manager, Azure Key Vault, HashiCorp Vault support
6. **Transformers/Plugins** - Load .NET assemblies with custom workflow stages for mapping and transformation
8. **Snapshot Testing** - Compare workflow outputs against expected snapshots
