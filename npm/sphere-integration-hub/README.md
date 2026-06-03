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

## Report and regression tooling

The CLI can generate JSON/HTML execution reports, create regression snapshots from known-good runs, compare later executions against those snapshots, and open an interactive report viewer with assertion diagnostics and baseline comparison.

```bash
sih snapshot create ./output/create-account.01J....workflow.report.json --name happy-path
sih report ./output --snapshot ./snapshots --no-open
```

## Learn more

- Repo and full docs: https://github.com/PinedaTec-EU/SphereIntegrationHub
- Getting started: https://github.com/PinedaTec-EU/SphereIntegrationHub/blob/main/.doc/getting-started.md
- MCP authoring quick reference: https://github.com/PinedaTec-EU/SphereIntegrationHub/blob/main/.doc/mcp-authoring-quick-reference.md
