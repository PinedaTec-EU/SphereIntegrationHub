# Swagger Catalog

The API catalog is a JSON array describing versions and their Swagger definitions. Each definition provides its own environment base URLs.

Location:

`src/resources/api-catalog.json`

Example:

```json
[
  {
    "version": "3.10",
    "definitions": [
      {
        "name": "example-service",
        "swaggerUrl": "/example/swagger/v1.0/swagger.json",
        "healthCheck": "/health",
        "readiness": {
          "maxRetries": 12,
          "delayMs": 5000,
          "timeoutMs": 2000,
          "httpStatus": [200, 204]
        },
        "baseUrl": {
          "local": "http://localhost:8081",
          "pre": "https://pre.api.example.com",
          "prod": "https://api.example.com"
        },
        "basePath": "/ocapi"
      }
    ]
  }
]
```

Notes:

- `version` matches the workflow `version`.
- `swaggerUrl` must be a relative path. It is resolved against `definitions[].baseUrl[env]` for the active environment.
- `definitions[].baseUrl` is required. It maps environment names to base URLs for that definition.
- `healthCheck` is optional. When present, SIH performs an HTTP `GET` before swagger caching and workflow execution. It can be an absolute URL or a relative path resolved against the definition base URL.
- `readiness` is optional. When present, SIH applies strict retry/timeout behavior during `healthCheck` probes and swagger downloads.
- `readiness.httpStatus` optionally overrides which health-check HTTP status codes count as healthy. If omitted, any `2xx` response is treated as healthy.
- `definitions[].basePath` is optional. When present, it is appended between the resolved base URL and the endpoint path.
- Swagger files are cached per version:
  - `src/resources/cache/{version}/{definition}.json`

Caching behavior:

- If the cache file exists and `--refresh-cache` is not specified, it is used.
- If the cache file does not exist, the CLI downloads the swagger and stores it in the cache.
- If `healthCheck` is configured and the readiness policy is exhausted, the run fails before execution.
