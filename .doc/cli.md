# CLI Usage

Basic options:

- `--workflow <path>`: workflow file to execute.
- `--env <environment>`: environment key for base URL (dev/pre/prod/etc.).
- `--catalog <path>`: optional catalog path.
- `--envfile <path>`: optional `.env` override for the root workflow.
- `--mocked`: use mock payloads/outputs when defined in stages.
- `--varsfile <path>`: optional workflow vars file (must be `.wfvars`).
- `--dry-run`: validate and print the execution plan (no HTTP calls).
- `--verbose`: detailed output for dry-run and cache operations.
- `--debug`: print stage debug sections before invocation.
- `--refresh-cache`: force re-download of swagger definitions.

Examples:

Dry-run:

```bash
SphereIntegrationHub.cli \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --dry-run \
  --verbose
```

Execute:

```bash
SphereIntegrationHub.cli \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --varsfile ./src/resources/workflows/create-account.wfvars
```

Override root `.env`:

```bash
SphereIntegrationHub.cli \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --envfile ./workflows/create-account.env
```

Use mocks:

```bash
SphereIntegrationHub.cli \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --mocked
```

Vars file auto-detection:

- If `--varsfile` is not provided and a file named `{workflow}.wfvars` exists alongside the workflow, it is used automatically.
- `.wfvars` can scope values by environment and version (see `variables.md`).
- `--verbose` prints the resolved source for each variable (global/environment/version).
