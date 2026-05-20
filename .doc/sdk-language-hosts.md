# SDK language hosts

SphereIntegrationHub SDKs for other languages must stay thin and workflow-first.

The rule is simple:

- the `.workflow` file remains the single source of truth
- the SDK only hosts execution
- the SDK must not introduce a second DSL for stages, branching, retries, outputs, or endpoint definitions

## Shared contract

Every language SDK should expose the same conceptual surface:

- `run(workflow)`
- `environment(...)`
- `input(...)`
- `inputs(...)`
- `varsFile(...)` or `vars_file(...)`
- `catalog(...)`
- `envFile(...)` or `env_file(...)`
- `mocked(...)`
- `verbose(...)`
- `debug(...)`
- `refreshCache(...)` or `refresh_cache(...)`
- `execute()`

Allowed semantics:

- select the workflow to execute
- choose environment
- supply runtime inputs
- resolve `.wfvars`
- optionally override `api.catalog`
- toggle execution flags already supported by the runtime

Forbidden semantics:

- `post(...)`, `get(...)`, `stage(...)`
- `output(...)`, `capture(...)`
- `forEach(...)`, `retry(...)`, `runIf(...)`
- any builder that creates workflow structure in code

## SDKs

- [`.NET SDK`](.doc/sdk-dotnet.md)
- [`TypeScript SDK`](.doc/sdk-typescript.md)
- [`Python SDK`](.doc/sdk-python.md)
- [`Rust SDK`](.doc/sdk-rust.md)
- [`Java SDK`](.doc/sdk-java.md)
- [`Go SDK`](.doc/sdk-go.md)
- [`PHP SDK`](.doc/sdk-php.md)
