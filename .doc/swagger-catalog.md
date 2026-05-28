# OpenAPI Catalog

The API catalog is a YAML list describing versions and their API contract definitions. Each definition provides its own environment base URLs and contract source.

Location:

`src/resources/api.catalog`

Example:

```yaml
- version: "3.10"
  assertionFailuresBlock: true
  definitions:
    - name: example-service
      contractType: openapi
      openApiUrl: /example/swagger/v1/swagger.json
      healthCheck: /health
      readiness:
        maxRetries: 12
        delayMs: 5000
        timeoutMs: 2000
        httpStatus: [200, 204]
      baseUrl:
        local: http://localhost:8081
        pre: https://pre.api.example.com
        prod: https://api.example.com
      basePath: /ocapi
  connections:
    - name: openai-main
      type: llm
      provider: openai
      baseUrl:
        local: https://api.openai.com/v1
      apiKeySecret: "{{input.openaiApiKey}}"
```

Notes:

- `version` matches the workflow `version`.
- `assertionFailuresBlock` is optional. When omitted, assertion failures are blocking by default. Set it to `false` to make assertion failures non-blocking for that catalog version unless overridden by CLI or by an individual assertion.
- `contractType` is optional. Allowed values are `openapi`, `swagger`, `scala`, and `llm`.
- URL fields supported by the catalog are `openApiUrl`, `swaggerUrl`, and `scalaUrl`.
- Use one contract URL field per definition. Relative contract URLs are resolved against `definitions[].baseUrl[env]` for the active environment.
- If `contractType` is omitted, SIH infers it from the populated URL field.
- `definitions[].baseUrl` is required. It maps environment names to base URLs for that definition.
- `healthCheck` is optional. When present, SIH performs an HTTP `GET` before contract caching and workflow execution. It can be an absolute URL or a relative path resolved against the definition base URL.
- `readiness` is optional. When present, SIH applies strict retry/timeout behavior during `healthCheck` probes and contract downloads.
- `readiness.httpStatus` optionally overrides which health-check HTTP status codes count as healthy. If omitted, any `2xx` response is treated as healthy.
- `definitions[].basePath` is optional. When present, it is appended between the resolved base URL and the endpoint path.
- `connections` is optional. Use it for non-OpenAPI resources such as LLM providers, queues, storage, SMTP, and other plugin-owned resources.
- `connections[].baseUrl` maps environment names to provider base URLs.
- `connections[].apiKeySecret` or `connections[].apiKey` is optional. Plugins such as `openai` can use it as a templated credential reference; mark the source workflow input as `secret: true`.
- `scala` changes URL-resolution/fallback conventions only. Downloaded contracts are still validated as OpenAPI/Swagger JSON.
- `llm` marks a legacy connection-style definition for plugins such as `openai`; prefer `connections` for new workflows.
- Contract files are cached per version:
  - `src/resources/cache/{version}/{definition}.json`

Caching behavior:

- If the cache file exists and `--refresh-cache` is not specified, it is used.
- If the cache file does not exist, the CLI downloads the contract and stores it in the cache.
- If `healthCheck` is configured and the readiness policy is exhausted, the run fails before execution.

## Assertion failure blocking

`assertionFailuresBlock` is a version-level execution default:

```yaml
- version: "3.11"
  assertionFailuresBlock: false
  definitions:
    - name: accounts
      openApiUrl: /swagger/v1/swagger.json
      baseUrl:
        local: http://localhost:8080
```

Precedence:

1. `assertions[].blocking`
2. CLI `--assertion-failures-block <true|false>`
3. selected catalog version `assertionFailuresBlock`
4. default `true`

Use the catalog value when an environment or version normally treats assertions as diagnostics. Use the CLI flag for one-off executions. Use `assertions[].blocking` when a specific assertion must be stricter or softer than the execution default.
