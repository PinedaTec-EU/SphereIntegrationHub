# SphereIntegrationHub Documentation

SphereIntegrationHub (SIH) is built for API integration testing in CI, reproducible API workflows, and OpenAPI contract validation pipelines.

It is a CLI tool that orchestrates API calls using a versioned OpenAPI catalog and YAML workflows. Use it when multi-step API scenarios need to be stored in Git, dry-run validated before execution, and reported consistently across local and CI/CD runs.

It supports:

- Versioned catalogs by environment
- Workflow composition (workflow stages calling other workflows)
- Context sharing between workflows
- Dry-run validation (schema, references, cached contract endpoints)
- API contract cache for offline validation and consistency
- Execution report artifacts (JSON + interactive HTML trace)
- Assertion diagnostics in reports, including blocking/non-blocking failures
- Regression snapshots and baseline comparison for known-good workflow runs
- Interactive report graph for zoomed-out workflow/stage navigation
- OpenTelemetry support (disabled by default)

Documentation index:

- Workflow schema: `workflow-schema.md`
- OpenAPI catalog: `swagger-catalog.md`
- CLI usage and report command: `cli.md`
- Execution reporting, snapshots, baselines, and assertions: `execution-reporting.md`
- Variables and context: `variables.md`
- Dry-run validation: `dry-run.md`
- OpenTelemetry: `telemetry.md`
