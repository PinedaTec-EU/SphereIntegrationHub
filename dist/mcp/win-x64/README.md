# SphereIntegrationHub Package

This folder contains a portable build for one target runtime (`RID`), including:

- `SphereIntegrationHub.MCP` (`.exe` on Windows): MCP server binary.
- `SphereIntegrationHub.cli` (`.exe` on Windows): workflow runtime CLI binary.
- `sih` (`sih.cmd` on Windows): unified launcher.
- `mcp` (`mcp.cmd` on Windows): shortcut launcher for MCP server.
- `AGENTS.md`: MCP behavioral instructions.

## Usage

Run workflows (CLI runtime):

```bash
./sih run --workflow ./src/resources/workflows/tiers_crud_workflow.workflow --env local --dry-run --refresh-cache
./sih run --workflow ./src/resources/workflows/tiers_crud_workflow.workflow --env local
```

Run MCP server:

```bash
./sih mcp
```

Or:

```bash
./mcp
```

## MCP client configuration

For editor/desktop MCP clients, point the command directly to:

- macOS/Linux: `<package_dir>/sih` with args `["mcp"]`
- Windows: `<package_dir>\\sih.cmd` with args `["mcp"]`

## Default Artifact Locations (LLM Guidance)

Use these paths first, without repository discovery:

- Resources root (default): `.sphere`
- API catalog: `.sphere/api-catalog.json`
- Swagger cache: `.sphere/cache`
- Workflows: `.sphere/workflows`
- Workflow vars: `<workflow_name>.wfvars` next to each workflow file

If these paths are not present, use legacy fallback:

- `src/resources/api-catalog.json`
- `src/resources/cache`
- `src/resources/workflows`

Environment overrides (if needed):

- `SIH_PROJECT_ROOT`
- `SIH_RESOURCES_PATH`
- `SIH_API_CATALOG_PATH`
- `SIH_CACHE_PATH`
- `SIH_WORKFLOWS_PATH`
