# Changelog

## [1.6.14.204] – 2026-04

- **Stronger CLI preflight and readiness reporting**:
  - `CliPipeline` now reports readiness and health-check state more clearly before workflow execution.
  - Preflight output makes it clearer which APIs have readiness policies enabled and how endpoints are resolved per environment.
  - Console reporting for health checks, Swagger download/refresh, and preflight failures was improved.
- **Clearer runtime errors and logging**:
  - Improved CLI error handling during preflight and execution.
  - Added a clearer final execution summary.
  - Failure messages are now more precise for health-check issues, Swagger download problems, and related validation failures.
- **Template resolution and validation improvements**:
  - Improved template resolution for environment-variable scenarios.
  - Tightened validation checks around template resolution and resource loading.
- **Telemetry and cache metrics**:
  - Adjusted cache metrics in `WorkflowLoader` and related tests to better reflect real hit/miss behavior.
- **MCP and tooling cleanup**:
  - Removed obsolete MCP server configuration.
  - Removed tool-version pinning logic from the GitHub Action workflow runner.
- **Documentation and samples**:
  - Updated README, docs, and samples to reflect readiness preflight, health checks, and the non-retriable `404` lookup case.
- **Published version line in this range**:
  - Includes the intermediate releases up to `1.6.14.202`.

## [1.6.13] – 2026-04

- **Validation caching** (three in-memory levels):
  - `ApiEndpointValidator`: static Swagger operations cache by file path with automatic invalidation by `lastWriteTime`; removes JSON re-parsing on every `Validate()` call.
  - `WorkflowLoader`: `WorkflowDocument` cache by path + `lastWriteTime`; workflows with `references.environmentFile` are excluded from the cache to ensure a change in the environment file is always detected.
  - `WorkflowValidatorService` (MCP): `ValidationResult` cache by SHA-256 hash of the YAML content; up to 50 entries with FIFO eviction.
- **OpenTelemetry metrics for cache** (`sih.cache.*`): hit/miss/eviction counters, size gauge, and duration histogram with tag `cache.hit=true/false` for all three levels. MCP exposes its own meter: `SphereIntegrationHub.MCP`.
- **`ApiHealthCheckProbe`**: full unit test coverage; `.sphere/workflows/workflows.config` file added to the project.
- **CLI path resolution for nested workflows**: `CliPathResolver` correctly resolves relative paths to sub-workflows in nested directories.
- **Execution report HTML** – cumulative improvements:
  - Visible duration on non-skipped stages in the timeline.
  - Duration centralized in timeline spans (without inline classes).
  - Improved CSS for the stage tree and skipped states.
  - Duration text condition fixed in the report generator.
- **Report branding**: SVG logo and project title in the HTML execution report header.
- Refactor: `ApiDefinition` handling consolidated and validation logic simplified.
- MCP: tool count and internal documentation updated.
- Docs: README with SVG icon, fixed NuGet badges, .NET 10 requirement, and `sih` command documentation.

## [1.5.13] – 2026-04-08

- **Readiness estricto en preflight**: `healthCheck` deja de ser solo informativo; ahora aplica retry/timeout configurables y aborta la ejecución si agota la política.
- **Nuevo bloque `readiness` en `api-catalog.json`**: soporta `maxRetries`, `delayMs`, `timeoutMs` y `httpStatus` por API.
- **Swagger download resiliente**: la descarga del swagger remoto reutiliza la política de readiness cuando existe.
- **Execution report**: añade bloque `Preflight` con operaciones, intentos consecutivos, retries y duración acumulada.
- **MCP actualizado**: `get_api_definitions`, generación de catálogo, upsert de catálogo y lectura de execution reports exponen la nueva superficie de readiness/preflight.
- **Documentación alineada**: README, catálogo, dry-run y GitHub Action documentan el nuevo contrato y el requisito de esperar readiness del deployment en CI/CD.

## [1.5.12] – 2026-04-07

- **GitHub Action** `run-sphere-workflow`: composite action to run workflows from any CI/CD pipeline with a fixed-version or latest option.
- **Improved endpoint pre-check**: when the tool starts, it lists the resolved base URLs for each API referenced in the workflow before starting Swagger caching, and emits them to the console immediately.
- **Secret masking**: values marked as `Secret` in inputs, variables, and outputs are masked in execution reports and in the console.
- **Offset support in date/time tokens**: `{{system:datetime.now+P1D}}`, `{{system:date.today-PT2H}}` with ISO 8601 durations.
- Fix MCP: fallback to version-level `baseUrl` when generating the catalog.
- MCP exposes the `Secret` flag in variable scope analysis and plugin capabilities.
- MCP documentation updated: real status (37 tools, all production levels).

## [1.5.9 – 1.5.11] – 2026-03

- **Improved execution report**: includes inputs, outputs, and per-stage results; multi-run selector in the HTML viewer.
- **`sih report` as a standalone command**: generates the interactive HTML report from any `.workflow.report.json` and opens it in the browser.
- HTML report with tabbed interface, dark mode, stage status indicators, visible application version, and layout improvements.
- Directory input support in `sih report` (loads all reports from the directory).
- `CliPipeline` refactored to emit messages progressively during execution instead of only at the end.

## [1.5.7 – 1.5.8] – 2026-02

- **Shared library** (`SphereIntegrationHub.Shared`): `ApiCatalogVersion` and `ApiDefinition` extracted as shared types between CLI and MCP, removing duplication.
- **Request body contract processing**: runtime validation of the body contract against the Swagger spec.
- **Per-definition `baseUrl`**: each catalog API defines its own environment-specific URLs (removes the single version-level URL); support for `basePath` and `{{port}}` token.
- **Swagger URI with templates**: `swaggerUrl` accepts `{{baseUrl}}`, `{{baseUrl.env}}`, and `{{port}}` tokens for dynamic URLs.
- MCP: catalog management tools (`upsert_api_catalog_and_cache`, `generate_api_catalog_file`, `refresh_swagger_cache_from_catalog`, `repair_workflow_artifacts`).
- MCP: variable analysis tools (`get_available_variables`, `analyze_context_flow`).
- Fix: improved error messages for Swagger download failures and missing cache file.

## [1.5.5 – 1.5.6] – 2026-02

- **Execution reporting**: persistence of each run as JSON + HTML with timeline, stage drill-down, HTTP capture with sensitive data redaction.
- **`sih report`**: first standalone command to generate the HTML trace report.
- **Structured inputs** (`Object`, `Array`): workflows that accept objects and arrays as typed input parameters.
- **`healthCheck` in catalog**: optional probe per API before Swagger caching; result visible at startup.
- **`ApiHealthCheckProbe`** integrated into the pipeline (dry-run and normal execution).
- MCP: report inspection tools (`list_execution_reports`, `read_execution_report`).
- MCP sample probe workflow (`mcp-probe.workflow`).

## [0.5.5] – 2026-01

- **`baseUrl` per-definition** (first iteration): APIs with their own environment-specific URLs in the catalog.
- Fix: URI validation when expanding templated Swagger URLs.
- Fix: improved error messages in Swagger download.
- Tests added for Swagger URI resolution with relative paths.

## [0.3.2] – 2025-12

- **MCP Server** published as a dotnet tool (`SphereIntegrationHub.Mcp.Tool`) with 26 tools across 4 capability levels (L1–L4).
- **CLI published as a dotnet tool** (`SphereIntegrationHub.Tool`, `sih` command).
- Automatic MCP tool registration via `[McpTool]` attribute.
- `CatalogUrlResolver` for dynamic Swagger URL resolution with port support.
- `IStageGenerator` extracted as an interface (DIP).
- Logging added to silent catch blocks in MCP.
- Telemetry and usage ping service (OpenTelemetry, opt-in).
- Migration to .NET 10.
- CI/CD: NuGet publishing pipeline.
- Unified distribution: `sih` launcher with MCP and CLI integrated.

## [0.3.1] – 2025-11

- **Initial MCP Server**: 26 tools for catalog exploration, workflow validation, stage generation, semantic analysis, system synthesis, and optimization.
- Idempotency tools: `ensure`, `expectedStatuses`, `onStatus`, `jumpOnStatus`.
- `forEach` with `bodyFile` / `dataFile` for collection bootstraps.
- Aggregated `forEach` results in workflow stages: `foreach_results`, `foreach_success_count`, `foreach_failed_count`.
- Failure propagation from child workflows to parent workflows.
- `runIf` expressions with functions: `exists()`, `empty()`, `coalesce()`, `first()`, `any()`, `jsonLength()`, `isEmptyJson()`.
- Optional path segments with `?` in tokens (`{{response.body.item.id?}}`).
- Token validation against mock payloads in dry-run.
- `Object` and `Array` as workflow input types.
- Sample files in `samples/` (parent/child, conditional, bootstrap, secrets).
- Branding renamed to SphereIntegrationHub.

## [0.3.0] – 2025-10 (baseline)

- Initial CLI: execution of YAML workflows against a versioned API catalog.
- Workflow composition: `Endpoint` and `Workflow` (child) stage types.
- Variables: `input`, `context`, `global`, `env`, `system`, stage outputs.
- Dry-run with endpoint validation against Swagger cache.
- Retry policies and circuit breakers per stage.
- `.wfvars` and `.env` for external inputs.
- API catalog with versioned Swagger caching.
- Initial unit tests.
