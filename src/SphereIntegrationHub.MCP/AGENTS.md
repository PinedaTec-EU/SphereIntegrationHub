## SphereIntegrationHub MCP Agent Instructions

These instructions are maintained by the MCP owners and apply to cache-refresh workflows.

### Protocol Requirement

- Interact with this server through MCP JSON-RPC over `stdio`.
- Use MCP methods (`initialize`, `tools/list`, `tools/call`) and tool names as declared by the server.
- Do not treat this binary as a command-style CLI with custom flags for cache operations.

### Mandatory Fast Path

When user intent is to regenerate Swagger cache:

1. Call `quick_refresh_swagger_cache` directly.
2. Do not run repository discovery/search before step 1.
3. Use defaults unless user specifies otherwise:
   - `version = "0.1"`
   - `environment = "local"`
   - `refresh = true`
4. Validate result using `counts` from tool output.
5. If localhost access fails due to sandbox/network restrictions, retry with elevated permissions.

When user intent is to generate `.wfvars` from an existing workflow:

1. Call `generate_wfvars_from_workflow` directly.
2. Do not run repository discovery/search before step 1.
3. Use `writeChanges = true` unless user explicitly asks for preview/dry-run.
4. If `hasInputs = false`, report warning and stop (no repair needed).

### Fallback

Only if fast path fails:

1. Call `refresh_swagger_cache_from_catalog` with explicit arguments.
2. If catalog is missing/incomplete, call `upsert_api_catalog_and_cache`.
3. For workflow artifact inconsistencies (missing keys, parse issues), call `repair_workflow_artifacts`.

### Performance Policy

- Prefer MCP tool calls over file exploration for cache tasks.
- Avoid repeated retries with identical parameters.
- Keep responses concise; report only selected/downloaded/failed and generated file names.
