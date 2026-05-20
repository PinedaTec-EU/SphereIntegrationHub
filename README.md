<p align="center">
  <a href="https://github.com/PinedaTec-EU/SphereIntegrationHub">
    <img loading="lazy" alt="Sphere Integration Hub" src="./.doc/SIH.png" width="85%"/>
  </a>
</p>

[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/PinedaTec-EU/SphereIntegrationHub)
[![License MIT](https://img.shields.io/badge/MIT_license-blue)](https://opensource.org/licenses/MIT)
[![npm](https://img.shields.io/npm/v/@pinedatec.eu/sphere-integration-hub?label=npm)](https://www.npmjs.com/package/@pinedatec.eu/sphere-integration-hub)
[![npm downloads](https://img.shields.io/npm/dm/@pinedatec.eu/sphere-integration-hub)](https://www.npmjs.com/package/@pinedatec.eu/sphere-integration-hub)
![MCP](https://img.shields.io/badge/MCP-35_tools-purple)
[![NuGet Version](https://img.shields.io/nuget/v/SphereIntegrationHub.Tool.svg?label=NuGet+CLI)](https://www.nuget.org/packages/SphereIntegrationHub.Tool/)
[![NuGet Version](https://img.shields.io/nuget/v/SphereIntegrationHub.MCP.Tool.svg?label=NuGet+MCP)](https://www.nuget.org/packages/SphereIntegrationHub.Mcp.Tool/)
[![GitHub Release](https://img.shields.io/github/v/release/PinedaTec-EU/SphereIntegrationHub?label=release)](https://github.com/PinedaTec-EU/SphereIntegrationHub/releases)
[![GitHub commits](https://img.shields.io/github/commit-activity/m/PinedaTec-EU/SphereIntegrationHub)](https://github.com/PinedaTec-EU/SphereIntegrationHub/commits/main)
[![GitHub Issues](https://img.shields.io/github/issues/PinedaTec-EU/SphereIntegrationHub)](https://github.com/PinedaTec-EU/SphereIntegrationHub/issues)
[![GitHub Stars](https://img.shields.io/github/stars/PinedaTec-EU/SphereIntegrationHub?style=social)](https://github.com/PinedaTec-EU/SphereIntegrationHub/stargazers)
[![Twitter Follow](https://img.shields.io/twitter/follow/jmrpineda?style=social)](https://twitter.com/jmrpineda)
[![LinkedIn](https://img.shields.io/badge/LinkedIn-Connect-blue?logo=linkedin)](https://www.linkedin.com/in/jmrpineda)

<p align="center">
  <img src="./.doc/icon.svg" width="90" height="90" alt="SphereIntegrationHub icon"/>
</p>

SphereIntegrationHub is a workflow-first runtime for orchestrating API calls with versioned OpenAPI catalogs, YAML workflows, reusable child workflows, dry-run validation, and execution reporting.

The runtime stays focused on orchestration. Protocol and channel behavior is delivered through plugins such as HTTP, OpenAI LLM, and secret providers like Vaultwarden.

## Start here

- [`Documentation hub`](.doc/index.md)
- [`Getting started`](.doc/getting-started.md)
- [`Workflow runtime semantics`](.doc/runtime-semantics.md)
- [`Workflow schema`](.doc/workflow-schema.md)
- [`MCP Server`](.doc/mcp-server.md)
- [`SDK language hosts`](.doc/sdk-language-hosts.md)
- [`Plugins`](.doc/plugins.md)

## Quick install

### npm / npx

```bash
npm install -g @pinedatec.eu/sphere-integration-hub
sih --version
```

```bash
npx @pinedatec.eu/sphere-integration-hub
```

### dotnet tool

```bash
dotnet tool install -g SphereIntegrationHub.Tool
dotnet tool install -g SphereIntegrationHub.Mcp.Tool
```

NuGet packages: [CLI](https://www.nuget.org/packages/SphereIntegrationHub.Tool/) · [MCP](https://www.nuget.org/packages/SphereIntegrationHub.Mcp.Tool/)

## First run

Validate a workflow without calling endpoints:

```bash
sih \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --dry-run \
  --verbose
```

Execute it with inputs:

```bash
sih \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --input username=user \
  --input password=secret \
  --input accountName=Acme
```

Generate JSON + HTML execution artifacts:

```bash
sih \
  --workflow ./src/resources/workflows/create-account.workflow \
  --env pre \
  --report-format both \
  --capture-http bodies
```

See [`getting-started.md`](.doc/getting-started.md) for install modes, CLI usage, GitHub Action usage, and SDK direction.

The planned language SDK contracts are documented in [`.doc/sdk-language-hosts.md`](.doc/sdk-language-hosts.md).

## Core concepts

- Workflows are plain YAML and remain the single source of truth for orchestration.
- API contracts live in versioned `api.catalog` definitions with cached OpenAPI documents.
- Validation can inspect workflows, references, and contract compatibility before runtime.
- Workflow stages can call endpoints, invoke child workflows, iterate collections, and expose outputs back to parent flows.
- Execution reports persist machine-readable JSON plus a self-contained HTML trace viewer.

## Examples

- [`samples/sample-bootstrap.workflow`](samples/sample-bootstrap.workflow): explicit `Http` plugin stage with plugin-specific `config`.
- [`samples/openai-llm/sample-openai-llm.workflow`](samples/openai-llm/sample-openai-llm.workflow): OpenAI plugin with `kind: LLM`, structured output, limits, timeout, and token usage.
- [`samples/workflows.config`](samples/workflows.config): explicit plugin activation and reporting defaults.
- [`samples/api.catalog`](samples/api.catalog): catalog definition with plugin binding.
- [`samples/vaultwarden-secrets`](samples/vaultwarden-secrets): Vaultwarden secret provider feeding `{{env:...}}` tokens.

More examples and usage patterns live in [`getting-started.md`](.doc/getting-started.md) and the sample files under [`samples/`](samples).

## Documentation map

### Product and adoption

- [`Overview`](.doc/overview.md)
- [`Why SphereIntegrationHub`](.doc/why-sih.md)
- [`Positioning and roadmap`](.doc/positioning-and-roadmap.md)
- [`Documentation hub`](.doc/index.md)

### Authoring and execution

- [`Getting started`](.doc/getting-started.md)
- [`Workflow schema`](.doc/workflow-schema.md)
- [`Variables and context`](.doc/variables.md)
- [`Dry-run validation`](.doc/dry-run.md)
- [`Workflow runtime semantics`](.doc/runtime-semantics.md)
- [`Execution reporting`](.doc/execution-reporting.md)
- [`Conditional expressions`](.doc/conditional-expressions.md)
- [`OpenAPI catalog`](.doc/swagger-catalog.md)
- [`SDK language hosts`](.doc/sdk-language-hosts.md)

### Integrations

- [`MCP Server`](.doc/mcp-server.md)
- [`GitHub Action`](.doc/github-action.md)
- [`Plugins`](.doc/plugins.md)
- [`HTTP plugin`](.doc/plugins-http.md)
- [`OpenAI LLM plugin`](.doc/plugins-openai.md)
- [`Secret providers`](.doc/secret-providers.md)
- [`Vaultwarden secret provider`](.doc/plugins-vaultwarden.md)
- [`OpenTelemetry`](.doc/telemetry.md)

## Community

If you use SphereIntegrationHub in your company or project, we would like to hear about it.

- Give the repository a star on GitHub.
- Share your use case on [LinkedIn](https://www.linkedin.com/in/jmrpineda) with `#SphereIntegrationHub`.
- Contact [sih@pinedatec.eu](mailto:sih@pinedatec.eu).
