# MCP Tools Reference (Current)

This document summarizes the current MCP surface implemented in `src/SphereIntegrationHub.MCP`.

## Tool Count

- Total tools: `35`
- L1: `27`
- L2: `5`
- L3: `1`
- L4: `2`

## Key Generation Tools

Catalog authoring notes for agents:
- Each definition provides its own `baseUrl` map (environment → absolute base URL). There is no version-level `baseUrl`.
- `swaggerUrl` must be a relative path (e.g. `/swagger/v1/swagger.json`). It is resolved against `baseUrl[env]` at runtime.
- API definitions may include optional `healthCheck`
- `healthCheck` can be an absolute URL or a relative path such as `/health`
- when present, runtime probes it before swagger caching and workflow execution and reports failures without aborting the run

### `generate_endpoint_stage`

Generates a SphereIntegrationHub `Endpoint` stage aligned with runtime schema (`kind`, `apiRef`, `httpVerb`, `expectedStatus`, `output`).

Authoring notes for agents:
- extend generated stages with `expectedStatuses` for idempotent flows
- use `onStatus` for non-success branches that still need outputs
- prefer `ensure` when the intent is "create if missing"
- use `bodyFile` when payloads are large
- use `forEach` / `dataFile` when seeding collections

Input options:
- Swagger mode: `version`, `apiName`, `endpoint`, `httpVerb`
- No-cache mode: `endpointSchema` (fallback from model knowledge)

### `generate_workflow_skeleton`

Generates a valid workflow skeleton with:
- `version`, `id`, `name`, `description`
- `input`
- `stages`
- `endStage`

The tool also accepts:
- `objectInputParameters`
- `arrayInputParameters`

And returns authoring hints for:
- `ensure`
- `expectedStatuses`
- `onStatus`
- `bodyFile`
- `dataFile`
- `forEach`
- `runIf` helper functions such as `exists`, `empty`, and `coalesce`
- structured JSON inputs

### `generate_mock_payload`

Generates JSON payload draft from endpoint body schema.

Input options:
- Swagger mode: `version`, `apiName`, `endpoint`, `httpVerb`
- No-cache mode: `endpointSchema`

### `generate_workflow_bundle`

Generates an automation bundle:
- `workflowDraft` (YAML)
- `wfvars` (YAML key/value)
- `payloadDrafts` (JSON examples for write endpoints)

Use this for end-to-end workflow authoring from developer directives.

### `write_workflow_artifacts`

Persists generated artifacts in configured workflows path:
- `.workflow`
- `.wfvars`
- optional payload files

Note: if `workflowPath` is provided with `.yaml`/`.yml` (or without extension), MCP normalizes it to `.workflow`.

### `repair_workflow_artifacts`

Repairs workflow artifacts from an existing workflow file:
- validates workflow YAML
- creates missing `.wfvars` if workflow has inputs
- repairs missing wfvars keys based on workflow inputs
- reports missing/extra keys and parse issues

### `generate_startup_bootstrap`

Generates startup bootstrap artifacts for API projects:
- CLI startup command
- `appsettings` section
- `IHostedService` class snippet
- `Program.cs` registration snippet

### `generate_api_catalog_file`

Generates `api-catalog.json` content and can write it directly to disk.
Use this first when onboarding MCP in repositories that do not have catalog files yet.

## Execution Report Tools

### `list_execution_reports`

Lists `.workflow.report.json` artifacts in the `output/` directory next to a workflow, ordered by most recent first.

Input options:

- `workflowPath` (string): path to the workflow file or its parent directory; SIH resolves the sibling `output/` folder automatically.
- `outputDir` (string): explicit path to the output directory (alternative to `workflowPath`).
- `limit` (integer): maximum entries to return (default: 10).

Use this to discover available execution artifacts before reading one.

### `read_execution_report`

Reads a `.workflow.report.json` artifact and returns execution metadata, metrics, and per-stage details.

Input options:

- `reportPath` (string, required): path to the `.workflow.report.json` file.
- `includeHttpBodies` (boolean): include request/response bodies in stage details (default: false — they can be large).

Output includes: `executionId`, `workflowName`, `result`, `durationMs`, `metrics`, `output`, and a `stages` array with per-stage status, HTTP details, retry count, jump target, and stage outputs.

Use this when the user asks why a workflow failed, what a stage returned, or what the result of a specific execution was.

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

### Variable Analysis Notes

- `get_available_variables` reports the effective `.env` values resolved for the workflow, including chained `{{env:NAME}}` references when they can be computed statically.
- `resolve_template_token` accepts environment tokens using the runtime syntax `{{env:NAME}}`.

## L2 Tools — Semantic Analysis

### `analyze_endpoint_dependencies`

Analyzes Swagger schemas to detect which endpoints need to be called before a target endpoint.

Required parameters: `version: string`, `apiName: string`, `endpoint: string`, `httpVerb: string`

### `infer_data_flow`

Analyzes multiple endpoints and maps response fields to request fields, building a data flow graph with stage bindings.

Required parameters: `version: string`, `endpoints: Array<{apiName, endpoint, httpVerb}>`

### `suggest_workflow_from_goal`

Generates a complete workflow YAML from a natural language goal using semantic analysis and endpoint relevance scoring.

Required parameters: `goal: string`
Optional parameters: `version: string`, `includeAuth: boolean` (default: true)

## L2 Tools — Pattern Detection

### `detect_api_patterns`

Detects common API patterns (OAuth, CRUD, pagination, filtering, batch) in an API specification.

Required parameters: `version: string`, `apiName: string`

### `generate_crud_workflow`

Generates a complete CRUD workflow for a resource based on detected patterns.

Required parameters: `apiName: string`, `resource: string`, `operations: Array<string>` (create, read, update, delete, list)
Optional parameters: `version: string`

## L3 Tools — Synthesis

### `synthesize_system_from_description`

Full autonomous system generation from a natural language description. Produces multiple workflow YAMLs, dependency graph, test scenarios, and API usage breakdown.

Required parameters: `description: string`
Optional parameters: `version: string`, `requirements: object` (`requiredApis`, `preferredApis`, `maxStagesPerWorkflow`, `includeAuthentication`, `includeErrorHandling`, `includeRetries`, `excludeEndpoints`, `performanceTarget`)

## L4 Tools — Optimization

### `suggest_optimizations`

Analyzes a workflow YAML and suggests improvements for parallelization, redundancy, resilience, caching, and batching. Returns current metrics, projected metrics, and prioritized recommendations.

Required parameters: `workflowYaml: string`
Optional parameters: `constraints: object` (`maxParallelism`, `prioritizeSpeed`, `prioritizeReliability`)

### `analyze_swagger_coverage`

Shows which API endpoints are used across all workflows and suggests use cases for unused endpoints. Returns coverage statistics broken down by API.

Required parameters: `version: string`
