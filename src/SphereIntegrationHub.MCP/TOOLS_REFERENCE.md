# MCP Tools Reference (Current)

This document summarizes the current MCP surface implemented in `src/SphereIntegrationHub.MCP`.

## Tool Count

- Total tools: `30`
- L1: `22`
- L2: `5`
- L3: `1`
- L4: `2`

## Key Generation Tools

### `generate_endpoint_stage`

Generates a SphereIntegrationHub `Endpoint` stage aligned with runtime schema (`kind`, `apiRef`, `httpVerb`, `expectedStatus`, `output`).

Input options:
- Swagger mode: `version`, `apiName`, `endpoint`, `httpVerb`
- No-cache mode: `endpointSchema` (fallback from model knowledge)

### `generate_workflow_skeleton`

Generates a valid workflow skeleton with:
- `version`, `id`, `name`, `description`
- `input`
- `stages`
- `endStage`

### `generate_mock_payload`

Generates JSON payload draft from endpoint body schema.

Input options:
- Swagger mode: `version`, `apiName`, `endpoint`, `httpVerb`
- No-cache mode: `endpointSchema`

### `generate_workflow_bundle`

Generates an automation bundle:
- `workflowDraft` (YAML)
- `wfvarsDraft` (YAML key/value)
- `payloadDrafts` (JSON examples for write endpoints)

Use this for end-to-end workflow authoring from developer directives.

### `write_workflow_artifacts`

Persists generated artifacts in configured workflows path:
- `.workflow`
- `.wfvars`
- optional payload files

### `generate_startup_bootstrap`

Generates startup bootstrap artifacts for API projects:
- CLI startup command
- `appsettings` section
- `IHostedService` class snippet
- `Program.cs` registration snippet

### `generate_api_catalog_file`

Generates `api-catalog.json` content and can write it directly to disk.
Use this first when onboarding MCP in repositories that do not have catalog files yet.

## Validation Tools

### `validate_workflow`

Validates workflow via:
- `workflowPath`, or
- `workflowYaml` (inline content)

### `plan_workflow_execution`

Builds execution plan via:
- `workflowPath`, or
- `workflowYaml` (inline content)

## Runtime Config (Paths)

The server supports configurable project paths via env vars:
- `SIH_PROJECT_ROOT`
- `SIH_RESOURCES_PATH`
- `SIH_API_CATALOG_PATH`
- `SIH_CACHE_PATH`
- `SIH_WORKFLOWS_PATH`

This enables running MCP against repositories that do not use `src/resources`.
