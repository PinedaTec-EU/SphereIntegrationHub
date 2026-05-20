# Getting Started

This guide pulls together installation, first execution, sample workflows, GitHub Action usage, and the current SDK direction.

## Installation

### npm / npx

The fastest path. No local .NET SDK is required.

Global install:

```bash
npm install -g @pinedatec.eu/sphere-integration-hub
sih --version
```

One-off via `npx`:

```bash
npx @pinedatec.eu/sphere-integration-hub
npx -p @pinedatec.eu/sphere-integration-hub sih --version
```

Pinned in a repository or CI:

```bash
npm install --save-dev @pinedatec.eu/sphere-integration-hub
```

### dotnet tool

If you already use .NET 10+:

```bash
dotnet tool install -g SphereIntegrationHub.Tool
dotnet tool install -g SphereIntegrationHub.Mcp.Tool
```

As a local tool:

```bash
dotnet new tool-manifest
dotnet tool install SphereIntegrationHub.Tool
dotnet tool restore
```

NuGet packages: [CLI](https://www.nuget.org/packages/SphereIntegrationHub.Tool/) · [MCP](https://www.nuget.org/packages/SphereIntegrationHub.Mcp.Tool/)

## First run

Dry-run validation:

```bash
sih \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --dry-run \
  --verbose
```

Run with mocks:

```bash
sih \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --mocked
```

Override the root `.env` source:

```bash
sih \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --envfile ./workflows/.env
```

Force refresh of the swagger cache:

```bash
sih \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --refresh-cache
```

Execute with inputs:

```bash
sih \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --input username=user \
  --input password=secret \
  --input accountName=Acme
```

Generate execution artifacts:

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

For the full CLI reference, see [`cli.md`](cli.md).

## Sample workflows

The repository includes reference workflows under `samples/`:

- `sample-parent.workflow` and `sample-child.workflow`: parent/child composition, workflow outputs/results, retries, circuit breakers, and conditional follow-up stages.
- `sample-conditional.workflow`: compound `runIf` with `&&`, `||`, parentheses, optional JSON paths, and safe checks for missing values.
- `sample-bootstrap.workflow`: `expectedStatuses`, `onStatus`, `ensure`, `bodyFile`, `dataFile`, and `forEach`.
- `sample-lookup-no-retry.workflow`: lookup flows where `404` is a business result and not a retryable transport failure.
- `sample-parent.wfvars`: companion vars example.
- `payloads/bootstrap-account.json` and `seed/accounts.json`: file-backed request and collection samples.
- `api.catalog`: reference catalog for the sample workflows.

Extra examples:

- [`../samples/fake-usage-seed.workflow`](../samples/fake-usage-seed.workflow): `forEach` plus `{{rand:*}}` fake data generation.
- [`../samples/openai-llm/sample-openai-llm.workflow`](../samples/openai-llm/sample-openai-llm.workflow): LLM stage example.
- [`../samples/sample-bootstrap.workflow`](../samples/sample-bootstrap.workflow): HTTP plugin stage with plugin config.

## GitHub Action

The `run-sphere-workflow` composite action installs the CLI and executes a workflow in GitHub Actions.

```yaml
- uses: PinedaTec-EU/SphereIntegrationHub/.github/actions/run-sphere-workflow@main
  with:
    workflow-path: ./workflows/create-account.workflow
    cli-args: --env prod --catalog ./api.catalog
```

Pre-deploy smoke test:

```yaml
- uses: PinedaTec-EU/SphereIntegrationHub/.github/actions/run-sphere-workflow@main
  with:
    workflow-path: ./workflows/smoke-test.workflow
    cli-args: --env prod --dry-run --verbose
```

Pin to a fixed version:

```yaml
- uses: PinedaTec-EU/SphereIntegrationHub/.github/actions/run-sphere-workflow@main
  with:
    workflow-path: ./workflows/onboard-customer.workflow
    tool-version: "1.5.12.149"
    cli-args: --env prod --catalog ./api.catalog --varsfile ./prod.wfvars
```

Upload execution artifacts:

```yaml
- uses: PinedaTec-EU/SphereIntegrationHub/.github/actions/run-sphere-workflow@main
  with:
    workflow-path: ./workflows/integration-test.workflow
    cli-args: --env pre --report-format both --capture-http bodies

- uses: actions/upload-artifact@v4
  if: always()
  with:
    name: sih-execution-report
    path: "*.workflow.report.*"
```

See [`github-action.md`](github-action.md) for the full reference.

## .NET SDK direction

The SDK direction remains workflow-first. The SDK should execute existing `.workflow` artifacts with the same runtime model used by the CLI and MCP server instead of introducing a second orchestration DSL.

Current shape:

```csharp
var result = await sihub
    .Run("./workflows/create-account.workflow")
    .Environment("local")
    .Input("email", "john@doe.com")
    .VarsFile("./workflows/create-account.wfvars")
    .ExecuteAsync();

var accountId = result.Output["accountId"];
```

Design rules:

- `sihub.Run(...)` and `sih.Run(...)` are entry points.
- The workflow remains the single source of truth for orchestration semantics.
- `api.catalog` is resolved by default from the workflow location.
- Catalog override is valid for tests and internal scenarios, but it is not the primary path.
- Inputs, environment selection, `.wfvars`, mock mode, and debug or verbose flags are execution parameters, not a second workflow language.
