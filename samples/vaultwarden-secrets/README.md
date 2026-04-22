# Vaultwarden secrets sample

This sample shows how to load secrets from the `vaultwarden` secret provider and expose them as `{{env:NAME}}` tokens.

Files:

- `workflows.config`: activates the `vaultwarden` secret provider and maps vault secret names to environment-variable names.
- `api.catalog`: regular catalog for the workflow version.
- `sample-vaultwarden-secrets.workflow`: consumes provider secrets via `{{env:ACCOUNTS_API_TOKEN}}`.

Notes:

- The current provider authenticates against a Vaultwarden-compatible identity endpoint and then calls a secrets projection endpoint.
- The projection endpoint is expected to return decrypted secret values as:

```json
{
  "secrets": {
    "accounts.api-token": "token-value",
    "accounts.client-secret": "secret-value"
  }
}
```

- Secrets loaded by the provider are added to the runtime secret register and are redacted from dry-run output and execution reports.
