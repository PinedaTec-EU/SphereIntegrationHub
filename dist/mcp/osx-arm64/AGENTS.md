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

### CLI Guidance Policy (Post-Generation)

When MCP has created/updated artifacts (catalog, cache, workflow, wfvars), guide the user to runtime execution with CLI:

1. Recommend `sih` launcher first (packaged runtime), not `dotnet run`.
2. Suggest validation before execution:
   - `./sih run --workflow <path> --env <env> --dry-run --refresh-cache`
3. Then suggest real execution:
   - `./sih run --workflow <path> --env <env>`
4. If `.wfvars` exists, include it explicitly when needed:
   - `--varsfile <path>`
5. Mention `dotnet run --project src/SphereIntegrationHub.cli` only as fallback when packaged `sih` is unavailable.
6. Persist workflows using `.workflow` extension (never default to `.yaml`/`.yml` for runtime workflow files).

Payload construction rules for `write_workflow_artifacts`:

- Build JSON so `workflowPath` always ends with `.workflow`.
- Build JSON so `wfvarsPath` always ends with `.wfvars` (when provided).
- If the user/model has a `.yaml`/`.yml` workflow name, convert it before tool call.
- Keep matching base names:
  - `abc123.workflow`
  - `abc123.wfvars`

When user asks “how do I run this workflow?”, provide concrete command lines with the actual workflow path produced by MCP.

### Autonomous CLI Execution Policy

When user intent clearly asks to execute/validate a generated workflow, the agent should execute CLI commands directly instead of only suggesting them.

Execution order:

1. Run validation first:
   - `./sih run --workflow <path> --env <env> --dry-run --refresh-cache`
2. If dry-run succeeds and user intent implies real execution, run:
   - `./sih run --workflow <path> --env <env>`

Guardrails:

- Prefer packaged launcher (`./sih`) when available.
- If packaged launcher is unavailable, fallback to:
  - `dotnet run --project src/SphereIntegrationHub.cli -- ...`
- Always include concrete workflow path and environment.
- If execution fails, report exact failing command and the actionable error summary.
- Do not silently skip dry-run unless user explicitly requests direct execution.
