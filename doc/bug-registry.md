# Bug Registry

Deprecated local bug registry. GitHub is the canonical source of truth, and `.doc/github-issues.md` is the local issue index for bugs, features, investigations, and technical debt.

This file only preserves historical imported bug references.

## SIB-001

- Discovery date: 2026-04-07
- Status: Fixed
- GitHub issue: [#3](https://github.com/PinedaTec-EU/SphereIntegrationHub/issues/3)
- Short description: CLI preflight used the launched workflow tree instead of the full selected catalog version for health checks and Swagger warm-up.

### Reproduction steps

1. Select a catalog version and environment containing multiple API definitions.
2. Launch a workflow that does not reference one of those definitions directly in the root workflow tree.
3. Observe that preflight skips the definition and execution can fail later when a nested workflow reaches it.

## SIB-002

- Discovery date: 2026-04-07
- Status: Fixed
- GitHub issue: [#4](https://github.com/PinedaTec-EU/SphereIntegrationHub/issues/4)
- Short description: The HTML execution report made nested workflow levels hard to read and rendered noisy text inside not-executed Gantt bars.

### Reproduction steps

1. Open an HTML execution report containing nested workflows and skipped or not-executed steps.
2. Inspect the hierarchy cues for nested workflow levels.
3. Inspect the Gantt bars for not-executed steps and observe the unreadable text treatment.
