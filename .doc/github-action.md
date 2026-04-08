# GitHub Action: Run SphereIntegrationHub Workflow

The `run-sphere-workflow` composite action installs the `SphereIntegrationHub.Tool` dotnet tool in a consumer repository and executes a workflow file. It always restores all tools from the local manifest (`.config/dotnet-tools.json`) and then updates the SIH tool — either to the latest published version or to a fixed version you specify.

## Action Reference

```
uses: PinedaTec-EU/SphereIntegrationHub/.github/actions/run-sphere-workflow@main
```

### Inputs

| Input | Required | Description |
|-------|----------|-------------|
| `workflow-path` | ✅ | Path to the `.workflow` file to execute, relative to the repo root. |
| `tool-version` | ❌ | Pin to a specific tool version (e.g. `1.5.12.149`). If omitted, the tool is updated to the **latest** published version. |
| `cli-args` | ❌ | Additional arguments forwarded verbatim to `sih`, e.g. `--env prod --catalog ./api-catalog.json`. |

### What the action does

1. **`dotnet tool restore`** — installs all tools listed in the repo's `.config/dotnet-tools.json`, so any other local tools are not affected.
2. If `tool-version` is set → `dotnet tool update SphereIntegrationHub.Tool --version <tool-version>` (pins to that exact version).
3. If `tool-version` is not set → `dotnet tool update SphereIntegrationHub.Tool` (updates to latest).
4. Runs `dotnet tool run sih --workflow <workflow-path> [cli-args]`.

### Deployment readiness

The action does not wait for infrastructure deployments to become ready. In CI/CD, gate this step behind the platform-native readiness signal first (`kubectl rollout status`, `kubectl wait`, container health checks, etc.). Once the action runs, SIH applies catalog-driven readiness retry/timeout rules for `healthCheck` probes and swagger downloads.

### Prerequisite

The consuming repository must have a `.config/dotnet-tools.json` that includes `SphereIntegrationHub.Tool`. If the file exists but the tool is not listed, `dotnet tool update` will install it automatically. If the manifest does not exist at all, `dotnet tool restore` will fail — in that case initialize the manifest first with `dotnet new tool-manifest`.

---

## Examples

### Minimal — run a workflow with latest tool

```yaml
name: Run SIH workflow

on:
  workflow_dispatch:

jobs:
  run-workflow:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.x'

      - uses: PinedaTec-EU/SphereIntegrationHub/.github/actions/run-sphere-workflow@main
        with:
          workflow-path: ./workflows/create-account.workflow
```

---

### Fixed version — pin to a known-good release

```yaml
      - uses: PinedaTec-EU/SphereIntegrationHub/.github/actions/run-sphere-workflow@main
        with:
          workflow-path: ./workflows/create-account.workflow
          tool-version: '1.5.12.149'
```

---

### With environment and catalog

```yaml
      - uses: PinedaTec-EU/SphereIntegrationHub/.github/actions/run-sphere-workflow@main
        with:
          workflow-path: ./workflows/onboard-customer.workflow
          cli-args: --env prod --catalog ./api-catalog.json --varsfile ./prod.wfvars
```

---

### Smoke test — dry-run as a deployment gate

```yaml
name: Pre-deploy smoke test

on:
  push:
    branches: [main]

jobs:
  smoke-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.x'

      - uses: PinedaTec-EU/SphereIntegrationHub/.github/actions/run-sphere-workflow@main
        with:
          workflow-path: ./workflows/smoke-test.workflow
          cli-args: --env prod --dry-run --verbose
```

---

### With execution report artifacts

```yaml
      - uses: PinedaTec-EU/SphereIntegrationHub/.github/actions/run-sphere-workflow@main
        with:
          workflow-path: ./workflows/integration-test.workflow
          cli-args: --env pre --catalog ./api-catalog.json --report-format both --capture-http bodies

      - uses: actions/upload-artifact@v4
        if: always()
        with:
          name: sih-execution-report
          path: '*.workflow.report.*'
```

---

### Secrets as environment variables

Use `--envfile` to load a `.env` file, or pass individual values via `env:` so they are picked up by `{{env:NAME}}` tokens in the workflow.

```yaml
      - uses: PinedaTec-EU/SphereIntegrationHub/.github/actions/run-sphere-workflow@main
        with:
          workflow-path: ./workflows/create-account.workflow
          cli-args: --env prod --catalog ./api-catalog.json
        env:
          API_KEY: ${{ secrets.API_KEY }}
          JWT_SECRET: ${{ secrets.JWT_SECRET }}
```

---

### Matrix — run multiple workflows in parallel

```yaml
jobs:
  run-workflows:
    runs-on: ubuntu-latest
    strategy:
      matrix:
        workflow:
          - ./workflows/create-account.workflow
          - ./workflows/bootstrap-orgs.workflow
          - ./workflows/smoke-test.workflow
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.x'

      - uses: PinedaTec-EU/SphereIntegrationHub/.github/actions/run-sphere-workflow@main
        with:
          workflow-path: ${{ matrix.workflow }}
          cli-args: --env pre --catalog ./api-catalog.json
```

---

## Choosing a version strategy

| Strategy | Input | When to use |
|----------|-------|-------------|
| Always latest | *(omit `tool-version`)* | Dev / staging pipelines — pick up fixes automatically. |
| Fixed version | `tool-version: '1.5.12.149'` | Production pipelines — predictable, auditable, reproducible. |

To find available versions, check the [NuGet page for SphereIntegrationHub.Tool](https://www.nuget.org/packages/SphereIntegrationHub.Tool).
