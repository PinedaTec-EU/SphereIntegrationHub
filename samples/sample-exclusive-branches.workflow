version: "3.11"
id: "01J9SAMPLEBRANCH000000001"
name: "sample-exclusive-branches"
description: >-
  Demonstrates mutually-exclusive branches with safe output resolution using
  all five runtime improvements: automatic null for skipped stages (1), coalesce
  in templates (2), safe navigation with ? (3), vars: derived variables (4),
  and onSkip.output (5). See .doc/conditional-expressions.md.
output: true
references:
  apis:
    - name: "subscriptions"
      definition: "subscriptions"
input:
  - name: "jwt"
    type: "Text"
    required: true
  - name: "planId"
    type: "Text"
    required: true
  - name: "tenantId"
    type: "Text"
    required: false
    description: "Optional tenant identifier. When provided, the tenant-scoped endpoint is used."

# ---------------------------------------------------------------------------
# Mejora 4: vars: block — canonical aliases for the exclusive branch outputs.
# Each var resolves lazily at the point of reference against the live context.
# downstream stages use {{var:subscriptionId}} instead of repeating coalesce().
# ---------------------------------------------------------------------------
vars:
  subscriptionId: >-
    {{coalesce(
      stage:create-subscription.output.subscriptionId,
      stage:create-subscription-with-tenant.output.subscriptionId
    )}}

# ---------------------------------------------------------------------------
# Branch A: create subscription on the default tenant.
#
# Mejora 5 — onSkip.output: when this branch is skipped (tenantId provided),
# register the value from branch B under the same key name. This way downstream
# stages can reference stage:create-subscription.output.subscriptionId directly
# without needing coalesce() at all.
# ---------------------------------------------------------------------------
stages:
  - name: "create-subscription"
    kind: "Endpoint"
    runIf: "{{input.tenantId}} == null || empty({{input.tenantId}})"
    apiRef: "subscriptions"
    endpoint: "/api/subscriptions"
    httpVerb: "POST"
    expectedStatuses: [201, 409]
    headers:
      Content-Type: "application/json"
      Authorization: "Bearer {{input.jwt}}"
    body: |
      {
        "planId": "{{input.planId}}"
      }
    ensure:
      mode: "CreateIfMissing"
      existsOn: [409]
    mock:
      status: 201
      payload: |
        {
          "subscriptionId": "sub-default-001",
          "status": "active"
        }
    output:
      subscriptionId: "{{response.body.subscriptionId?}}"
      status:         "{{response.body.status?}}"
    onSkip:
      output:
        # Mirrors branch B's output so downstream code needs no coalesce
        subscriptionId: "{{stage:create-subscription-with-tenant.output.subscriptionId}}"
        status:         "{{stage:create-subscription-with-tenant.output.status}}"

# ---------------------------------------------------------------------------
# Branch B: create subscription under an explicit tenant.
# Also uses onSkip.output to mirror branch A when this branch is not taken.
# ---------------------------------------------------------------------------
  - name: "create-subscription-with-tenant"
    kind: "Endpoint"
    runIf: "{{input.tenantId}} != null && !empty({{input.tenantId}})"
    apiRef: "subscriptions"
    endpoint: "/api/tenants/{{input.tenantId}}/subscriptions"
    httpVerb: "POST"
    expectedStatuses: [201, 409]
    headers:
      Content-Type: "application/json"
      Authorization: "Bearer {{input.jwt}}"
    body: |
      {
        "planId": "{{input.planId}}",
        "tenantId": "{{input.tenantId}}"
      }
    ensure:
      mode: "CreateIfMissing"
      existsOn: [409]
    mock:
      status: 201
      payload: |
        {
          "subscriptionId": "sub-tenant-002",
          "status": "active"
        }
    output:
      subscriptionId: "{{response.body.subscriptionId?}}"
      status:         "{{response.body.status?}}"
    onSkip:
      output:
        subscriptionId: "{{stage:create-subscription.output.subscriptionId}}"
        status:         "{{stage:create-subscription.output.status}}"

# ---------------------------------------------------------------------------
# Activate: uses {{var:subscriptionId}} — single declaration, no repetition.
# Mejora 3 safe nav used in runIf as an extra guard.
# ---------------------------------------------------------------------------
  - name: "activate-subscription"
    kind: "Endpoint"
    # Mejora 4: var: token — no inline coalesce needed in runIf either
    runIf: "{{var:subscriptionId}} != ''"
    apiRef: "subscriptions"
    endpoint: "/api/subscriptions/activate"
    httpVerb: "POST"
    expectedStatuses: [200]
    headers:
      Content-Type: "application/json"
      Authorization: "Bearer {{input.jwt}}"
    body: |
      {
        "subscriptionId": "{{var:subscriptionId}}"
      }
    mock:
      status: 200
      payload: |
        { "activated": true }
    output:
      activated: "{{response.body.activated}}"

# ---------------------------------------------------------------------------
# Close: also uses {{var:subscriptionId}} in the endpoint path.
# Mejora 3 safe key nav (?) on close result — endpoint returns 204 (no body).
# ---------------------------------------------------------------------------
  - name: "close-subscription"
    kind: "Endpoint"
    runIf: "{{stage:activate-subscription.output.activated}} == 'true'"
    apiRef: "subscriptions"
    endpoint: "/api/subscriptions/{{var:subscriptionId}}/close"
    httpVerb: "DELETE"
    expectedStatuses: [200, 204]
    headers:
      Authorization: "Bearer {{input.jwt}}"
    mock:
      status: 200
      payload: |
        { "closed": true }
    output:
      closed: "{{response.body.closed?}}"

endStage:
  output:
    # Mejora 4: var: — single token, readable and DRY
    subscriptionId: "{{var:subscriptionId}}"
    # Mejora 5 + Mejora 1: thanks to onSkip.output, both stage names always carry
    # a value; the one that ran has the real value, the other mirrors it.
    branchAStatus: "{{stage:create-subscription.output.status}}"
    branchBStatus: "{{stage:create-subscription-with-tenant.output.status}}"
    activated:     "{{stage:activate-subscription.output.activated?}}"
    closed:        "{{stage:close-subscription.output.closed?}}"
