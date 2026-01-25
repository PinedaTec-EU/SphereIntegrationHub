---
name: workflow-drafting
description: Draft SphereIntegrationHub workflows and matching .wfvars files from a user request. Use when the user asks to create or update workflow YAML, needs a workflow plan based on API endpoints (from api-catalog + swagger cache), or wants a workflow draft/wfvars draft for a specific action (e.g., create account/media).
---

# Workflow Drafting

## Overview

Generate two aligned drafts for SphereIntegrationHub: a workflow YAML draft and a `.wfvars` draft. Locate the right endpoint(s) in the API catalog + swagger cache, infer prerequisite steps (login, organization validation, etc.), and ask for any missing details.

## Workflow Drafting Process

Follow this sequence and ask short clarifying questions when you cannot complete a step confidently.

1. Confirm the user goal and scope.
   - Identify the action ("create media", "create account", etc.).
   - Ask for missing business rules or required fields not implied by the API spec.
   - Ask for environment/version preferences if not provided.

2. Locate the endpoint(s).
   - Read `src/resources/api-catalog.json` to find the API definition name and version.
   - Use cached swagger: `src/resources/cache/<version>/<definition>.json` to find method/path, auth, and request/response schemas.
   - If no cached swagger exists, ask the user for the swagger source or to provide the endpoint details.

3. Determine prerequisites and dependency workflows.
   - Inspect existing workflows in `src/resources/workflows/` for reusable steps (e.g., `login.workflow`, `GetOrCreate_Organization.workflow`).
   - Heuristics: if endpoint requires auth, include login; if account creation requires organization validation, include the org workflow; if org workflow requires login, ensure login precedes it.
   - If unsure about prerequisites, ask a targeted question before drafting.

4. Draft the workflow YAML.
   - Use a unique `id` (ULID-like) and a kebab-case `name`.
   - Populate `references.workflows` and `references.apis` with relative paths/names.
   - Define `input` entries for all user-supplied values and any required credential fields.
   - Add `initStage` variables/context if you need globals (timestamps, org ids, tokens).
   - Add a `Workflow` stage for login (and other nested flows), with `workflowRef` and `inputs`.
   - Add `Endpoint` stages with `apiRef`, `endpoint`, `httpVerb`, `expectedStatus`, `headers`, `body`, `output`.
   - Add `mock` payloads or outputs if needed for testability.
   - If `output: true`, include `endStage.output` with meaningful keys.

5. Draft the `.wfvars` file.
   - Include all required inputs with placeholder or example values.
   - Keep input names exactly aligned with `input` entries in the workflow draft.

6. Ask for missing pieces.
   - Any uncertain endpoint path, required headers, or request body fields must be confirmed with the user.
   - Ask for auth details (token source, credential fields) if not already known.

## Draft Output Format

Always return two drafts, labeled clearly:
1. `workflow-draft` (YAML)
2. `wfvars-draft` (YAML key/value)

## Implementation Notes

- Use existing workflows in `src/resources/workflows/` as stylistic and structural references.
- Keep references relative to the workflow file location.
- Ensure stage names are unique and stable (used by output tokens).
- If you include `output: true`, ensure `endStage.output` is populated.
- If a nested workflow version differs from the parent, add `allowVersion`.

## Questions to Ask When Blocked

- Which API definition/version should I target from `api-catalog.json`?
- Which endpoint path and HTTP verb should be used for the action?
- What auth mechanism applies (JWT from login, other token)?
- Are there required organization/account prerequisites beyond login?
- What fields are mandatory in the request body and what sample values should I use?
