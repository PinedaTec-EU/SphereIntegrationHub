# Secret Providers

SphereIntegrationHub can load secret values before the workflow is parsed and make them available through `{{env:NAME}}` tokens.

## Configuration

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

Behavior:

- Secret providers run before the workflow file is loaded.
- If any configured secret provider fails, the runtime aborts immediately and does not continue to workflow loading, validation, or execution.
- Resolved secrets are merged into the inherited environment dictionary.
- Workflows can reference them through `{{env:ACCOUNTS_API_TOKEN}}`.
- Loaded secret values are registered for masking in dry-run output and execution reports.

## Vaultwarden provider

The `vaultwarden` provider currently follows a pragmatic two-step flow:

1. Authenticate against a Vaultwarden-compatible token endpoint such as `/identity/connect/token`.
2. Call a secrets projection endpoint that returns decrypted values in a simple JSON map.

Example response:

```json
{
  "secrets": {
    "accounts.api-token": "token-value",
    "accounts.client-secret": "secret-value"
  }
}
```

Why this shape:

- Vaultwarden stores vault items encrypted.
- Full client-side decryption is intentionally kept out of the runtime in this initial provider implementation.
- The provider is therefore suitable when your Vaultwarden deployment exposes a trusted companion endpoint that projects decrypted secrets for automation use.

## Example

See [samples/vaultwarden-secrets/sample-vaultwarden-secrets.workflow](/Users/jmr.pineda/Projects/GitHub/PinedaTec.eu/SphereIntegrationHub/samples/vaultwarden-secrets/sample-vaultwarden-secrets.workflow) and its sibling [workflows.config](/Users/jmr.pineda/Projects/GitHub/PinedaTec.eu/SphereIntegrationHub/samples/vaultwarden-secrets/workflows.config).
