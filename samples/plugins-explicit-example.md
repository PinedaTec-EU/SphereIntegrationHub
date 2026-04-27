# Explicit plugin example

This sample shows the three pieces needed when plugin activation is explicit:

1. `workflows.config`

```yaml
plugins:
  - http
```

2. `api.catalog`

```yaml
- version: 3.11
  plugins:
    - id: http
      contractVersion: "1.0"
      runtimeVersion: "1.0"
  definitions:
    - name: accounts
      contractType: openapi
      openApiUrl: /swagger/v1/swagger.json
      baseUrl:
        local: http://localhost:5000
```

3. workflow stage

```yaml
stages:
  - name: "seed-accounts"
    kind: "Http"
    expectedStatuses: [201, 409]
    config:
      apiRef: "accounts"
      endpoint: "/api/accounts"
      httpVerb: "POST"
      headers:
        Authorization: "Bearer {{input.jwt}}"
```

Resolution flow:

- `workflows.config` activates the plugin id `http`
- `api.catalog` declares that `http` is valid for that catalog version and contract
- the runtime loads the plugin and asks it which `kind` values it owns
- the stage `kind: "Http"` is routed to that plugin

If `plugins:` is omitted in `workflows.config`, `http` is enabled by default.
