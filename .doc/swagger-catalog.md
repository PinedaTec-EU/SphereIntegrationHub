# Swagger Catalog

The API catalog is a JSON array describing versions, environment base URLs, and Swagger definitions.

Location:

`src/resources/api-catalog.json`

Example:

```json
[
  {
    "version": "3.10",
    "baseUrl": {
      "dev": "https://dev.api.example.com",
      "pre": "https://pre.api.example.com",
      "prod": "https://api.example.com"
    },
    "definitions": [
      {
        "name": "example-service",
        "swaggerUrl": "/example/swagger/v1.0/swagger.json",
        "baseUrl": {
          "local": "http://localhost:8081"
        },
        "basePath": "/ocapi"
      }
    ]
  }
]
```

Notes:

- `version` matches the workflow `version`.
- `swaggerUrl` can be absolute or relative. Relative URLs are resolved against `definitions[].baseUrl[env]` when defined, otherwise `baseUrl[env]`.
- `definitions[].baseUrl` is optional. When present, it overrides `baseUrl` for that definition and environment.
- `definitions[].basePath` is optional. When present, it is appended between the resolved base URL and the endpoint path.
- Swagger files are cached per version:
  - `src/resources/cache/{version}/{definition}.json`

Caching behavior:

- If the cache file exists and `--refresh-cache` is not specified, it is used.
- If the cache file does not exist, the CLI downloads the swagger and stores it in the cache.
