# Sphere Integration Hub

NPM wrapper for the Sphere Integration Hub CLI and MCP server.

The package installs the `sih` and `sih-mcp` commands and downloads the matching
platform binaries from the GitHub release identified by `sihReleaseVersion`.

```bash
npm install -g @pinedatec.eu/sphere-integration-hub
sih --version
sih-mcp
```

Repository: https://github.com/PinedaTec-EU/SphereIntegrationHub

## Report and regression tooling

The CLI can generate JSON/HTML execution reports, create regression snapshots from known-good runs, compare later executions against those snapshots, and open an interactive report viewer with assertion diagnostics and baseline comparison.

```bash
sih snapshot create ./output/create-account.01J....workflow.report.json --name happy-path
sih report ./output --snapshot ./snapshots --no-open
```
