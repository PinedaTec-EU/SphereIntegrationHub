version: "3.11"
id: "01J9SAMPLECONTROL000000001"
name: "sample-conditional-control"
description: "Demonstrates compound runIf expressions, safe missing-token checks, optional JSON paths, and control helpers."
output: true
references:
  apis:
    - name: "accounts"
      definition: "accounts"
input:
  - name: "jwt"
    type: "Text"
    required: true
  - name: "accountLogin"
    type: "Text"
    required: true
  - name: "preferredTag"
    type: "Text"
    required: false
  - name: "fallbackTag"
    type: "Text"
    required: false

stages:
  - name: "lookup-account"
    kind: "Endpoint"
    apiRef: "accounts"
    endpoint: "/api/accounts"
    httpVerb: "GET"
    expectedStatus: 200
    headers:
      Authorization: "Bearer {{input.jwt}}"
    query:
      login: "{{input.accountLogin}}"
    mock:
      payload: |
        {
          "items": [],
          "meta": {
            "requestId": "01J9LOOKUPREQ000000000001"
          }
        }
    output:
      dto: "{{response.body}}"
      requestId: "{{response.body.meta.requestId?}}"

  - name: "create-account"
    kind: "Endpoint"
    runIf: "jsonLength({{stage:lookup-account.output.dto.items?}}) == 0 && {{stage:lookup-account.output.requestId}} != null"
    apiRef: "accounts"
    endpoint: "/api/accounts"
    httpVerb: "POST"
    expectedStatuses: [201, 409]
    headers:
      Content-Type: "application/json"
      Authorization: "Bearer {{input.jwt}}"
    body: |
      {
        "login": "{{input.accountLogin}}"
      }
    ensure:
      mode: "CreateIfMissing"
      existsOn: [409]
      output:
        exists: "true"
    mock:
      status: 409
      payload: |
        {
          "appId": "existing-001",
          "status": "already-exists"
        }
    output:
      dto: "{{response.body}}"
      accountAppId: "{{response.body.appId?}}"

  - name: "preview-tag"
    kind: "Endpoint"
    runIf: "(coalesce({{input.preferredTag}}, {{input.fallbackTag}}, '') != '') && ({{stage:create-account.output.accountAppId}} != null || {{stage:create-account.output.ensure_status}} == 'existing')"
    apiRef: "accounts"
    endpoint: "/api/accounts/{{stage:create-account.output.accountAppId}}/tag-preview"
    httpVerb: "GET"
    expectedStatus: 200
    headers:
      Authorization: "Bearer {{input.jwt}}"
    query:
      preferredTag: "{{input.preferredTag}}"
      fallbackTag: "{{input.fallbackTag}}"
    mock:
      payload: |
        {
          "tagged": true
        }
    output:
      tagResult: "{{response.body}}"

endStage:
  output:
    lookup: "{{stage:lookup-account.output.dto}}"
    account: "{{stage:create-account.output.dto}}"
    accountAppId: "{{stage:create-account.output.accountAppId}}"
    ensureStatus: "{{stage:create-account.output.ensure_status}}"
