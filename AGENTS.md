# AGENTS

This repository consumes shared conventions from `../ai-skills-shared`.

## Official Source

- Shared rules live in `../ai-skills-shared/AGENTS.md`.
- Shared skills live in `../ai-skills-shared/.shared-skills/skills/*`.
- Shared conventions live in `../ai-skills-shared/rules/conventions/*`.
- Do not duplicate shared rules in this repository unless the deviation is local and explicit.

## Local Version Bump Rule

- This repository already provides a local .NET tool for version bumps: `dotnet tool run versionbumper`.
- Prefer `dotnet tool run versionbumper` over manual edits when a task requires bumping the repository version after a compile, build, test, or validation milestone.
- Treat `versionbumper` as the default bump mechanism for the main four-part repo version and aligned `.csproj` `ReleaseVersion` values.
- Do not assume `versionbumper` completes the full publish/release workflow by itself.
- When the change is part of npm or release publication work, also follow `PROJECT.md` and `README.md` for the additional release-surface files and sequencing, especially `npm/sphere-integration-hub/package.json` `sihReleaseVersion` and `scripts/release.sh`.

## Priority Order

1. System or tool-session instructions.
2. Provider-specific instructions (`CLAUDE.md`, `COPILOT.md`, `CODEX.md`, `.codex/AGENTS.md`).
3. This `AGENTS.md`.
4. `../ai-skills-shared/AGENTS.md`.
5. Applicable shared skills in `../ai-skills-shared/.shared-skills/skills/*`.
6. The user prompt for the current task.
