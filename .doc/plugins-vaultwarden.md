# Vaultwarden Secret Provider Plugin

The built-in `vaultwarden` plugin is a secret provider. It runs before workflow loading and exposes resolved values as `{{env:NAME}}` tokens.

## Activation

Secret providers are declared in `workflows.config`:

```yaml
secretProviders:
  - plugin: vaultwarden
    config:
      baseUrl: "https://vaultwarden.example.test"
      usernameEnv: "VAULTWARDEN_USERNAME"
      passwordEnv: "VAULTWARDEN_PASSWORD"
      tokenPath: "/identity/connect/token"
      secretsPath: "/api/sih/secrets"
      mappings:
        ACCOUNTS_API_TOKEN: "accounts.api-token"
        ACCOUNTS_CLIENT_SECRET: "accounts.client-secret"
```

## Behavior

- Secret providers run before the workflow file is loaded.
- If the provider fails, the runtime aborts before validation or execution.
- Resolved secrets are merged into the workflow environment dictionary.
- Workflows reference them through `{{env:ACCOUNTS_API_TOKEN}}`.
- Loaded secret values are registered for masking in dry-run output and execution reports.

## Vaultwarden Flow

The plugin follows a pragmatic two-step flow:

1. Authenticate against a Vaultwarden-compatible token endpoint such as `/identity/connect/token`.
2. Call a secrets projection endpoint that returns decrypted values in a JSON map.

Expected secrets response:

```json
{
  "secrets": {
    "accounts.api-token": "token-value",
    "accounts.client-secret": "secret-value"
  }
}
```

The provider intentionally does not implement full client-side Vaultwarden item decryption. It is intended for deployments that expose a trusted companion endpoint for automation-grade decrypted secret projection.

See [`samples/vaultwarden-secrets/sample-vaultwarden-secrets.workflow`](../samples/vaultwarden-secrets/sample-vaultwarden-secrets.workflow) and [`samples/vaultwarden-secrets/workflows.config`](../samples/vaultwarden-secrets/workflows.config).
