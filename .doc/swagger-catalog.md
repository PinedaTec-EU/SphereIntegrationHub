# OpenAPI Catalog

The API catalog is a YAML list describing versions and their API contract definitions. Each definition provides its own environment base URLs and contract source.

Location:

`src/resources/api.catalog`

Example:

```yaml
- version: "3.10"
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
```

Notes:

- `version` matches the workflow `version`.
- `contractType` is optional. Allowed values are `openapi`, `swagger`, and `scala`.
- URL fields supported by the catalog are `openApiUrl`, `swaggerUrl`, and `scalaUrl`.
- Use one contract URL field per definition. Relative contract URLs are resolved against `definitions[].baseUrl[env]` for the active environment.
- If `contractType` is omitted, SIH infers it from the populated URL field.
- `definitions[].baseUrl` is required. It maps environment names to base URLs for that definition.
- `healthCheck` is optional. When present, SIH performs an HTTP `GET` before contract caching and workflow execution. It can be an absolute URL or a relative path resolved against the definition base URL.
- `readiness` is optional. When present, SIH applies strict retry/timeout behavior during `healthCheck` probes and contract downloads.
- `readiness.httpStatus` optionally overrides which health-check HTTP status codes count as healthy. If omitted, any `2xx` response is treated as healthy.
- `definitions[].basePath` is optional. When present, it is appended between the resolved base URL and the endpoint path.
- `scala` changes URL-resolution/fallback conventions only. Downloaded contracts are still validated as OpenAPI/Swagger JSON.
- Contract files are cached per version:
  - `src/resources/cache/{version}/{definition}.json`

Caching behavior:

- If the cache file exists and `--refresh-cache` is not specified, it is used.
- If the cache file does not exist, the CLI downloads the contract and stores it in the cache.
- If `healthCheck` is configured and the readiness policy is exhausted, the run fails before execution.
