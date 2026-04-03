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
- `definitions[].basePath` is optional. When present, it is appended between the resolved base URL and the endpoint path.
- Swagger files are cached per version:
  - `src/resources/cache/{version}/{definition}.json`

Caching behavior:

- If the cache file exists and `--refresh-cache` is not specified, it is used.
- If the cache file does not exist, the CLI downloads the swagger and stores it in the cache.
