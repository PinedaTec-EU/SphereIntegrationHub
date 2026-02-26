# SphereIntegrationHub MCP Server

Model Context Protocol (MCP) server for SphereIntegrationHub providing AI-assisted workflow development.

## Overview

This MCP server exposes 32 tools organized in 4 levels (L1-L4) that enable LLMs to:
- Explore API catalogs and generate workflow stages
- Validate and analyze workflows
- Optimize execution strategies
- Design complex integration systems

## Agent Instructions (Maintained with MCP)

Agent-specific operating rules are maintained in:

- `src/SphereIntegrationHub.MCP/AGENTS.md`

Keep that file as the authoritative source for cache-refresh fast paths and low-token behavior.

## Architecture

```
McpServer (stdio JSON-RPC)
├── Core/
│   ├── IMcpTool.cs           - Tool interface
│   ├── McpToolAttribute.cs   - Tool metadata
│   └── McpRequest.cs         - JSON-RPC types
├── Services/
│   ├── Catalog/              - API catalog & Swagger reading
│   ├── Validation/           - Workflow validation
│   ├── Generation/           - Stage & workflow generation
│   └── Analysis/             - Variable scope & context flow
└── Tools/
    ├── CatalogTools.cs       - 4 catalog exploration tools
    ├── ValidationTools.cs    - 3 validation tools
    ├── GenerationTools.cs    - 3 generation tools
    ├── AnalysisTools.cs      - 3 analysis tools
    ├── ReferenceTools.cs     - 2 reference tools
    └── DiagnosticTools.cs    - 3 diagnostic tools
```

## Level 1 Tools (24 implemented)

### Catalog Tools (4)
- `list_api_catalog_versions` - Lists available API versions
- `get_api_definitions` - Gets APIs for a version
- `get_api_endpoints` - Lists endpoints in an API
- `get_endpoint_schema` - Gets detailed endpoint schema

### Validation Tools (3)
- `validate_workflow` - Validates workflow YAML
- `validate_stage` - Validates single stage
- `plan_workflow_execution` - Analyzes execution plan

### Generation Tools (12)
- `generate_endpoint_stage` - Generates stage from endpoint
- `generate_workflow_skeleton` - Creates workflow template
- `generate_mock_payload` - Generates test payload
- `generate_workflow_bundle` - Generates `workflow` + `.wfvars` + payload drafts
- `write_workflow_artifacts` - Writes generated artifacts to disk
- `generate_wfvars_from_workflow` - Generates `.wfvars` from workflow `input`
- `repair_workflow_artifacts` - Validates workflow and creates/repairs `.wfvars`
- `generate_startup_bootstrap` - Generates startup integration for app boot
- `generate_api_catalog_file` - Generates/writes `api-catalog.json`
- `upsert_api_catalog_and_cache` - Creates/updates catalog from swagger URL and downloads cache
- `refresh_swagger_cache_from_catalog` - Downloads cache files from existing catalog
- `quick_refresh_swagger_cache` - Fast-path cache refresh with defaults (`version=0.1`, `environment=local`, `refresh=true`)

### Analysis Tools (3)
- `get_available_variables` - Shows available variables at a point
- `resolve_template_token` - Resolves template tokens
- `analyze_context_flow` - Analyzes context flow

### Reference Tools (2)
- `list_available_workflows` - Lists all workflows
- `get_workflow_inputs_outputs` - Shows workflow I/O

### Diagnostic Tools (3)
- `explain_validation_error` - Explains errors with suggestions
- `get_plugin_capabilities` - Lists stage types & features
- `suggest_resilience_config` - Suggests retry/timeout config

## Community

If SphereIntegrationHub is helping your team integrate APIs faster, let us know!

- Give us a ⭐ on [GitHub](https://github.com/PinedaTec-EU/SphereIntegrationHub)
- Share your experience on [LinkedIn](https://www.linkedin.com/in/jmrpineda) mentioning **#SphereIntegrationHub**
- Send us a note at [sih@pinedatec.eu](mailto:sih@pinedatec.eu)

> The CLI (`sih run`) collects anonymous usage stats (version, OS, run count) at most once every 7 days.
> Opt out: `SIH_USAGE_PING=0`

## Quick Start

### Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or higher

### Step 1: Clone and build

```bash
git clone https://github.com/PinedaTec-EU/SphereIntegrationHub.git
cd SphereIntegrationHub
dotnet build src/SphereIntegrationHub.MCP
```

### Step 1.1: Build portable package (`sih` launcher)

To package both the MCP server and runtime CLI in one distributable folder:

```bash
./dist/mcp/build-mcp.sh
```

This generates per-platform packages under `dist/mcp/<rid>/` with:

- `sih` (`sih.cmd` on Windows): unified launcher
- `SphereIntegrationHub.MCP` (`.exe` on Windows)
- `SphereIntegrationHub.cli` (`.exe` on Windows)
- `mcp` (`mcp.cmd` on Windows)

Usage:

```bash
./dist/mcp/osx-arm64/sih mcp
./dist/mcp/osx-arm64/sih run --workflow ./src/resources/workflows/create-account.workflow --env local --dry-run --refresh-cache
```

### Step 2: Open the project in your IDE and the MCP is ready

The repository includes **pre-configured MCP files** for the most common AI agents. No manual configuration needed:

| Agent | Config file (included) | Action needed |
|-------|----------------------|---------------|
| **Claude Code** | `.mcp.json` | Open project, reload Claude Code |
| **GitHub Copilot** | `.vscode/mcp.json` | Open project in VS Code, open Copilot Chat in Agent mode |
| **Codex (OpenAI)** | `.codex/config.toml` | Open project with Codex CLI (project must be trusted) |

Just build the solution and open the project in your IDE. The MCP server will be discovered automatically.

### Step 3: Start talking

No special syntax needed. Just ask naturally:

> *"What APIs are available in version 3.11?"*
> *"Generate a workflow to create an account with payment"*
> *"Validate this workflow and tell me what's wrong"*

The agent decides which MCP tools to call on your behalf.

## First Workflow via LLM -> MCP (Recommended Script)

Use this short conversation flow with your LLM to get the first workflow generated and written to disk.

1. Ensure MCP is connected:
   - Prompt: `List available API catalog versions using MCP.`
2. If catalog/cache is missing, bootstrap from swagger URL:
   - Prompt: `Use MCP tool upsert_api_catalog_and_cache with version 3.11, apiName AccountsAPI, swaggerUrl <YOUR_SWAGGER_URL>, basePath /api/accounts.`
3. If catalog exists and you only need cache refresh:
   - Prompt: `Use MCP tool refresh_swagger_cache_from_catalog for version 3.11 with refresh=true.`
   - Fast-path prompt (recommended for local setups): `Use MCP tool quick_refresh_swagger_cache.`
4. Generate workflow draft bundle:
   - Prompt: `Use MCP to generate a workflow bundle for "create account" and include workflowDraft + wfvars.`
5. Persist artifacts:
   - Prompt: `Use MCP write_workflow_artifacts to save the generated .workflow and .wfvars under the workflows path.`
6. Validate:
   - Prompt: `Validate the generated workflow with MCP and show me any fixes needed.`

This is the minimum path for first-time users: bootstrap catalog/cache, generate, write, validate.

---

## Pre-included Configuration Files

The following MCP configuration files are included in the repository and work out of the box after building:

### `.mcp.json` (Claude Code)

Used by Claude Code (VS Code extension and CLI). Detected automatically when you open the project.

### `.vscode/mcp.json` (GitHub Copilot)

Used by GitHub Copilot Chat in Agent mode. Detected automatically when you open the project in VS Code.

### `.cursor/mcp.json` (Cursor)

Used by Cursor IDE. Detected automatically when you open the project in Cursor.

### `.codex/config.toml` (OpenAI Codex)

Used by Codex CLI and IDE extension. Detected automatically when the project is trusted. Uses TOML format instead of JSON.

> All four files use relative paths, so they work regardless of where you clone the repository.

---

## Desktop Apps (manual configuration required)

Desktop apps don't have a workspace concept, so they require **absolute paths** and manual setup.

### ChatGPT Desktop

Open **Settings > MCP Servers > Add Server** and fill in:

| Field | Value |
|-------|-------|
| Name | `sphere-integration-hub` |
| Command | `/absolute/path/to/SphereIntegrationHub/dist/mcp/<rid>/sih` |
| Arguments | `mcp` |
| Environment | `SIH_PROJECT_ROOT=/absolute/path/to/SphereIntegrationHub` |

### Claude Desktop

Edit the config file:

- **macOS:** `~/Library/Application Support/Claude/claude_desktop_config.json`
- **Windows:** `%APPDATA%\Claude\claude_desktop_config.json`

```json
{
  "mcpServers": {
    "sphere-integration-hub": {
      "command": "/absolute/path/to/SphereIntegrationHub/dist/mcp/<rid>/sih",
      "args": [
        "mcp"
      ],
      "env": {
        "SIH_PROJECT_ROOT": "/absolute/path/to/SphereIntegrationHub"
      }
    }
  }
}
```

---

## Verifying the Setup

Once the agent is running with the MCP connected, ask it:

> *"List the available API catalog versions"*

If the server is working, the agent will call `list_api_catalog_versions` and return the available versions (e.g., `3.10`, `3.11`). If something is wrong, the agent will show the connection error.

You can also test the server manually from the terminal:

```bash
echo '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}' | SIH_PROJECT_ROOT=. ./dist/mcp/osx-arm64/sih mcp
```

This should print a JSON response listing all 32 tools.

## Runtime Path Configuration

MCP can run against any repository layout by setting environment variables:

- `SIH_PROJECT_ROOT`: base path used to resolve relative values.
- `SIH_RESOURCES_PATH`: optional resources root.
- `SIH_API_CATALOG_PATH`: explicit `api-catalog.json` path.
- `SIH_CACHE_PATH`: explicit swagger cache folder.
- `SIH_WORKFLOWS_PATH`: explicit workflows folder.
- `SIH_MCP_PROFILE`: optional MCP tool profile (`full` default, `cache` for cache-only toolset).

Default resolution when env vars are not provided:
- Preferred default: `<project>/.sphere`
- Backward-compatible fallback: `<project>/src/resources` (if legacy structure exists and `.sphere` is not present)

Inside the selected resources root:
- catalog: `<resources>/api-catalog.json`
- cache: `<resources>/cache`
- workflows: `<resources>/workflows`

### Token/Latency Optimization: Cache Profile

If your agent mainly regenerates Swagger cache, set:

`SIH_MCP_PROFILE=cache`

This exposes only a minimal subset of tools:
- `list_api_catalog_versions`
- `generate_api_catalog_file`
- `upsert_api_catalog_and_cache`
- `refresh_swagger_cache_from_catalog`
- `quick_refresh_swagger_cache`

This reduces `tools/list` payload and usually lowers discovery tokens/latency in generic LLM agents.

Example (`.vscode/mcp.json`) for a different repository:

```json
{
  "servers": {
    "sphere-integration-hub": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/path/to/SphereIntegrationHub/src/SphereIntegrationHub.MCP"
      ],
      "env": {
        "SIH_PROJECT_ROOT": "${workspaceFolder}",
        "SIH_API_CATALOG_PATH": "${workspaceFolder}/automation/catalog/api-catalog.json",
        "SIH_CACHE_PATH": "${workspaceFolder}/automation/cache",
        "SIH_WORKFLOWS_PATH": "${workspaceFolder}/automation/workflows"
      }
    }
  }
}
```

## Required Bootstrap for MCP Workflow Generation

For MCP to generate workflows autonomously (discover APIs/endpoints and build drafts), you must have:
- `api-catalog.json` available.
- Swagger cache files available in the configured cache path.

Without cache, MCP cannot inspect endpoint catalogs/schemas directly. In that case, generation only works if the model receives manual `endpointSchema` input.

## Preload Swagger Cache With CLI (No Endpoint Execution)

Recommended mandatory step before asking the LLM to generate workflows with MCP:

Use:

```bash
SphereIntegrationHub.cli \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --dry-run \
  --refresh-cache \
  --verbose
```

Why this works:
- `--dry-run` validates only (no endpoint execution).
- `--refresh-cache` forces swagger download/update for the API definitions referenced by the workflow.

If you do not have a workflow yet, create a minimal bootstrap workflow that only contains `references.apis` for the definitions you want to cache, then run the same command.

Example bootstrap workflow:

```yaml
version: "3.11"
id: "bootstrap-cache-01"
name: "bootstrap-cache"
description: "Used only to refresh swagger cache."
output: false
references:
  apis:
    - name: "accounts"
      definition: "accounts"
stages: []
endStage: {}
```

## Manual Fallback (Advanced)

If cache is still unavailable, generation tools can run only with explicit `endpointSchema` supplied by the model/user:
- `generate_endpoint_stage`
- `generate_mock_payload`
- `generate_workflow_bundle`

This fallback is useful for controlled/manual scenarios, but it is not the default autonomous workflow generation path.

## Swagger URL Warning (Important)

When generating or updating catalog definitions, `swaggerUrl` must point to the OpenAPI JSON document, not to the Swagger UI HTML page.

- Valid examples:
  - `https://host/service/swagger/v1/swagger.json`
  - `https://host/service/openapi.json`
- Invalid example:
  - `https://host/service/swagger/index.html`

MCP now validates downloaded content before writing cache.

If the source returns HTML, MCP first tries common JSON fallback patterns automatically:
- same path + `/v1/swagger.json`
- same path + `/swagger.json`
- same path + `/openapi.json`

If none of those return a valid OpenAPI document, the tool fails with a clear error.

MCP also emits warnings to stderr when HTML is detected:
- On startup: if `api-catalog.json` already contains `swaggerUrl` entries that look like HTML/UI URLs.
- During cache download: when an HTML response is received and fallback resolution starts.

When using `upsert_api_catalog_and_cache`, if `apiName` looks generic (for example `api-5009`), MCP tries to infer a better name from OpenAPI `info.title` so catalog entries and cache filenames are more readable.

## No Catalog Bootstrap

If `api-catalog.json` does not exist, server startup no longer fails. Use:
- `generate_api_catalog_file`

to create the catalog first, then continue with endpoint/workflow generation.

## Dependencies

- **ModelContextProtocol** (0.6.0) - MCP SDK
- **System.Text.Json** (9.0.0) - JSON serialization
- **YamlDotNet** (16.3.0) - YAML parsing
- **SphereIntegrationHub.cli** (project reference) - Reuses CLI services

## Project Structure

- `McpServer.cs` - Main server class handling JSON-RPC
- `Program.cs` - Entry point
- `Core/` - MCP protocol implementation
- `Services/` - Business logic services
- `Tools/` - Tool implementations
- `Models/` - Data models
- `Services/Integration/SihServicesAdapter.cs` - Bridge to CLI services

## Development

### Adding New Tools

1. Create tool class implementing `IMcpTool`
2. Add `[McpTool]` attribute
3. Register in `McpServer.RegisterTools()`
4. Implement `ExecuteAsync()` with argument validation

Example:

```csharp
[McpTool("my_tool", "Description", Category = "MyCategory", Level = "L1")]
public sealed class MyTool : IMcpTool
{
    public string Name => "my_tool";
    public string Description => "Description";

    public object InputSchema => new
    {
        type = "object",
        properties = new { param = new { type = "string" } },
        required = new[] { "param" }
    };

    public async Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var param = arguments?.GetValueOrDefault("param")?.ToString()
            ?? throw new ArgumentException("param is required");

        // Implementation
        return new { result = "..." };
    }
}
```

### Testing

```bash
# Build
dotnet build src/SphereIntegrationHub.MCP

# Run tests
dotnet test src/SphereIntegrationHub.MCP

# Manual test with echo
echo '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}' | \
  dotnet run --project src/SphereIntegrationHub.MCP
```

## Error Handling

All tools follow consistent error handling:
- Missing required arguments → `ArgumentException`
- File not found → `FileNotFoundException`
- Invalid data → `InvalidOperationException`
- Errors returned as JSON-RPC error responses

## Logging

Diagnostic logs written to stderr:
- Tool registration
- Request processing
- Error details

Does not interfere with JSON-RPC on stdout.

## License

Part of SphereIntegrationHub project.
