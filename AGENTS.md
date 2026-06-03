# AGENTS

This repository consumes shared conventions from `../ai-skills-shared`.

## Official Source

- Shared rules live in `../ai-skills-shared/AGENTS.md`.
- Shared skills live in `../ai-skills-shared/.shared-skills/skills/*`.
- Shared conventions live in `../ai-skills-shared/rules/conventions/*`.
- Do not duplicate shared rules in this repository unless the deviation is local and explicit.

## Priority Order

1. System or tool-session instructions.
2. Provider-specific instructions (`CLAUDE.md`, `COPILOT.md`, `CODEX.md`, `.codex/AGENTS.md`).
3. This `AGENTS.md`.
4. `../ai-skills-shared/AGENTS.md`.
5. Applicable shared skills in `../ai-skills-shared/.shared-skills/skills/*`.
6. The user prompt for the current task.
