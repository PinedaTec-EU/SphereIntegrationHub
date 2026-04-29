# HTTP Stage Plugin

The built-in `http` plugin executes HTTP endpoint stages. It owns `kind: Http` and `kind: Endpoint`.

## Activation

```yaml
plugins:
  - http
```

If `plugins` is omitted, `http` is enabled by default for compatibility and the runtime emits a transition warning.

Declare the plugin and API definition in `api.catalog`:

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

## Stage

```yaml
stages:
  - name: "create-account"
    kind: "Http"
    expectedStatus: 201
    config:
      apiRef: "accounts"
      endpoint: "/api/accounts"
      httpVerb: "POST"
      headers:
        Content-Type: "application/json"
        Authorization: "Bearer {{input.jwt}}"
      body: |
        { "name": "{{input.accountName}}" }
    output:
      dto: "{{response.body}}"
      accountId: "{{response.body.id?}}"
```

`bodyFile` can be used instead of `body`, and both support template resolution.

## Preflight

The HTTP plugin uses OpenAPI/Swagger preflight:

- environment validation through `definitions[].baseUrl`
- optional health checks
- Swagger cache download/reuse
- endpoint and verb validation
- optional required parameter validation during dry-run

See [`samples/sample-bootstrap.workflow`](../samples/sample-bootstrap.workflow) for a larger example with `forEach`, `bodyFile`, `expectedStatuses`, `onStatus`, and `ensure`.
