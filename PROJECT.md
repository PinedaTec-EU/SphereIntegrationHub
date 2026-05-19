# SphereIntegrationHub Project Conventions

## Packaging and release

This repository produces artifacts through three distinct distribution channels. Future work that changes versioning, packaging, publishing, release automation, install flows, package names, or publish scripts must preserve these rules unless the owner explicitly changes the release model.

### Version source of truth

- The authoritative release version is stored in:
  - `version_definition.json`
  - `version.nfo`
- The canonical repo version is a four-part version such as `1.7.20.279`.
- When the change is expected to be published to npm, the repo version must advance in a way that changes the first three version components too. The repository convention for that case is `+0.0.1.1` instead of `+0.0.0.1`.
- Reason: the npm package version is derived from the repo version by dropping the fourth component, so a bump like `1.7.20.279 -> 1.7.20.280` would still map to the already-published npm version `1.7.20` and would collide.
- Example:
  - internal-only or non-npm bump: `1.7.20.279 -> 1.7.20.280`
  - release that will publish to npm: `1.7.20.279 -> 1.7.21.280`
- For the main release line, the same four-part version must be kept aligned in the `ReleaseVersion` property of the following projects:
  - `src/SphereIntegrationHub.cli/SphereIntegrationHub.cli.csproj`
  - `src/SphereIntegrationHub.MCP/SphereIntegrationHub.MCP.csproj`
  - `src/SphereIntegrationHub.Shared/SphereIntegrationHub.Shared.csproj`
  - `src/SphereIntegrationHub.HttpPlugin/SphereIntegrationHub.HttpPlugin.csproj`
  - `src/SphereIntegrationHub.VaultwardenPlugin/SphereIntegrationHub.VaultwardenPlugin.csproj`
  - `src/SphereIntegrationHub.Telemetry/SphereIntegrationHub.Telemetry.csproj`
- `src/SphereIntegrationHub.OpenAIPlugin/SphereIntegrationHub.OpenAIPlugin.csproj` is currently versioned independently in the repository and must not be silently normalized to the main line without explicit intent.

### Distribution channels

#### 1. GitHub Release binaries

- `scripts/release.sh` is the canonical script for building the self-contained binary release artifacts.
- A release tag is mandatory for publishable versions. The canonical tag format is `v<four-part-version>`.
- The release tag must be created from the commit that already contains:
  - the final version bump files
  - the aligned `ReleaseVersion` values
  - the npm package version update when npm is part of the release
- Never create the release tag from a dirty working tree state. Git tags point to commits, not to uncommitted files.
- `scripts/create_newtag.sh` is the repo helper that creates `v<version>` from `version.nfo` and pushes it.
- It publishes two .NET entrypoints:
  - `SphereIntegrationHub.cli` as the `sih` executable
  - `SphereIntegrationHub.MCP` as the `sih-mcp` executable
- It builds these runtime identifiers:
  - `linux-x64`
  - `linux-arm64`
  - `osx-x64`
  - `osx-arm64`
  - `win-x64`
- Release archives produced in `dist/` are:
  - `sih-linux-x64.tar.gz`
  - `sih-linux-arm64.tar.gz`
  - `sih-darwin-x64.tar.gz`
  - `sih-darwin-arm64.tar.gz`
  - `sih-win32-x64.zip`
- The GitHub release tag format is always `v<four-part-version>`, for example `v1.7.21.280`.
- `scripts/release.sh` can run in `--build-only` mode to create `dist/` without publishing.

#### 2. npm package

- The npm package lives in `npm/sphere-integration-hub`.
- Package name: `@pinedatec.eu/sphere-integration-hub`
- The npm package does not embed the native binaries in Git. Instead, `postinstall.js` downloads the matching archives from GitHub Releases.
- Because npm accepts only three-part semver for this package flow, `scripts/release.sh` maps the four-part repo version to npm by dropping the fourth component:
  - repo version `1.7.20.279`
  - npm version `1.7.20`
- Because of that mapping, any release intended for npm publication must bump the repo version so the first three components change. In this repository, that means using the `+0.0.1.1` convention for npm-bound releases.
- This means the publish order matters:
  1. commit the release version changes
  2. create and push the Git tag `v<four-part-version>`
  3. build archives
  4. create GitHub Release with `v<four-part-version>`
  5. publish npm package with the derived three-part version
- Any future change to archive names, GitHub tag naming, npm package version mapping, or `postinstall.js` download URLs must be treated as a breaking packaging change.

#### 3. NuGet packages

- NuGet publishing is currently handled separately from `scripts/release.sh`.
- The repo-local helper is `scripts/local/publish-nuget-local.sh`.
- That script:
  - loads `.env.local` when present
  - discovers every packable `*.csproj` under `src/`
  - runs `dotnet pack` for each packable project
  - pushes generated `.nupkg` files with `dotnet nuget push`
- It requires `NUGET_API_KEY` or `--api-key`.
- It defaults to `https://api.nuget.org/v3/index.json` but allows `--source`.
- `Telemetry` is not packable today. The currently packable projects under `src/` are:
  - `SphereIntegrationHub.Tool` from `src/SphereIntegrationHub.cli`
  - `SphereIntegrationHub.Mcp.Tool` from `src/SphereIntegrationHub.MCP`
  - shared/plugin packages discovered from the remaining packable projects
- Changes to `IsPackable`, `PackageId`, or tool packaging behavior must be evaluated against this script.

### Tool package conventions

- CLI NuGet tool:
  - project: `src/SphereIntegrationHub.cli/SphereIntegrationHub.cli.csproj`
  - `PackageId`: `SphereIntegrationHub.Tool`
  - `PackAsTool`: `true`
  - command: `sih`
- MCP NuGet tool:
  - project: `src/SphereIntegrationHub.MCP/SphereIntegrationHub.MCP.csproj`
  - `PackageId`: `SphereIntegrationHub.Mcp.Tool`
  - `PackAsTool`: `true`
  - command: `sih-mcp`

### Automation expectations

- If a task changes packaging, release flow, version files, package ids, npm install behavior, release archives, or publish scripts, inspect all of these together before editing:
  - `version_definition.json`
  - `version.nfo`
  - `scripts/release.sh`
  - `scripts/local/publish-nuget-local.sh`
  - `npm/sphere-integration-hub/package.json`
  - `npm/sphere-integration-hub/postinstall.js`
  - every affected `*.csproj`
- Do not assume that publishing npm also covers NuGet, or vice versa.
- Do not assume npm uses the four-part version; it intentionally truncates to three parts.
- Do not change release archive filenames without updating npm install behavior.
- Do not create a release tag before the release commit exists.
