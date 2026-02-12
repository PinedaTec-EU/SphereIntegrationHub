# SphereIntegrationHub MCP Server

Model Context Protocol (MCP) server for SphereIntegrationHub providing AI-assisted workflow development.

## Overview

This MCP server exposes 26 tools organized in 4 levels (L1-L4) that enable LLMs to:
- Explore API catalogs and generate workflow stages
- Validate and analyze workflows
- Optimize execution strategies
- Design complex integration systems

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

## Level 1 Tools (18 implemented)

### Catalog Tools (4)
- `list_api_catalog_versions` - Lists available API versions
- `get_api_definitions` - Gets APIs for a version
- `get_api_endpoints` - Lists endpoints in an API
- `get_endpoint_schema` - Gets detailed endpoint schema

### Validation Tools (3)
- `validate_workflow` - Validates workflow YAML
- `validate_stage` - Validates single stage
- `plan_workflow_execution` - Analyzes execution plan

### Generation Tools (3)
- `generate_endpoint_stage` - Generates stage from endpoint
- `generate_workflow_skeleton` - Creates workflow template
- `generate_mock_payload` - Generates test payload

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

## Quick Start

### Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or higher

### Step 1: Clone and build

```bash
git clone https://github.com/PinedaTec-EU/SphereIntegrationHub.git
cd SphereIntegrationHub
dotnet build src/SphereIntegrationHub.MCP
```

### Step 2: Open the project in your IDE and the MCP is ready

The repository includes **pre-configured MCP files** for the most common AI agents. No manual configuration needed:

| Agent | Config file (included) | Action needed |
|-------|----------------------|---------------|
| **Claude Code** | `.mcp.json` | Open project, reload Claude Code |
| **GitHub Copilot** | `.vscode/mcp.json` | Open project in VS Code, open Copilot Chat in Agent mode |
| **Cursor** | `.cursor/mcp.json` | Open project in Cursor |
| **Codex (OpenAI)** | `.codex/config.toml` | Open project with Codex CLI (project must be trusted) |

Just build the solution and open the project in your IDE. The MCP server will be discovered automatically.

### Step 3: Start talking

No special syntax needed. Just ask naturally:

> *"What APIs are available in version 3.11?"*
> *"Generate a workflow to create an account with payment"*
> *"Validate this workflow and tell me what's wrong"*

The agent decides which MCP tools to call on your behalf.

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
| Command | `dotnet` |
| Arguments | `run --project /absolute/path/to/SphereIntegrationHub/src/SphereIntegrationHub.MCP` |
| Environment | `SIH_PROJECT_ROOT=/absolute/path/to/SphereIntegrationHub` |

### Claude Desktop

Edit the config file:

- **macOS:** `~/Library/Application Support/Claude/claude_desktop_config.json`
- **Windows:** `%APPDATA%\Claude\claude_desktop_config.json`

```json
{
  "mcpServers": {
    "sphere-integration-hub": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/absolute/path/to/SphereIntegrationHub/src/SphereIntegrationHub.MCP"
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
echo '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}' | \
  SIH_PROJECT_ROOT=. dotnet run --project src/SphereIntegrationHub.MCP
```

This should print a JSON response listing all 26 tools.

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
