# SphereIntegrationHub Documentation

SphereIntegrationHub is a CLI tool that orchestrates API calls using a versioned Swagger catalog and YAML workflows. It supports:

- Versioned catalogs by environment
- Workflow composition (workflow stages calling other workflows)
- Context sharing between workflows
- Dry-run validation (schema, references, swagger endpoints)
- Swagger cache for offline validation and consistency
- OpenTelemetry support (disabled by default)

Documentation index:

- Workflow schema: `workflow-schema.md`
- Swagger catalog: `swagger-catalog.md`
- CLI usage: `cli.md`
- Variables and context: `variables.md`
- Dry-run validation: `dry-run.md`
- OpenTelemetry: `telemetry.md`
