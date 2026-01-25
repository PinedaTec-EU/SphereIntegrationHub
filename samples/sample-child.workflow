version: "3.11"
id: "01J9SAMPLECHILD00000000001"
name: "sample-child"
description: "Creates an account and optionally attaches a tag."
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
  - name: "accountPassword"
    type: "Text"
    required: true
  - name: "tag"
    type: "Text"
    required: true
initStage:
  variables:
    - name: requestId
      type: "Ulid"
    - name: organizationAppId
      type: "Fixed"
      value: "MTCW"

stages:
  - name: "create-account"
    kind: "Endpoint"
    apiRef: "accounts"
    endpoint: "/api/accounts"
    httpVerb: "POST"
    expectedStatus: 200
    headers:
      Content-Type: "application/json-patch+json"
      Authorization: "Bearer {{input.jwt}}"
    body: |
      {
        "login": "{{input.accountLogin}}",
        "password": "{{input.accountPassword}}",
        "organizationAppId": "{{global:organizationAppId}}"
      }
    mock:
      status: 200
      payloadFile: "./mock-create-account.json"
    output:
      dto: "{{response.body}}"
    set:
      accountAppId: "{{stage:json(create-account.output.dto).appId}}"
    message: "Account created for {{input.accountLogin}}."
    jumpOnStatus:
      409: "endStage"

  - name: "attach-tag"
    kind: "Endpoint"
    runIf: "{{input.tag}} != null"
    apiRef: "accounts"
    endpoint: "/api/accounts/{{global:organizationAppId}}/tag"
    httpVerb: "POST"
    expectedStatus: 200
    headers:
      Content-Type: "application/json"
      Authorization: "Bearer {{input.jwt}}"
    body: |
      {
        "accountAppId": "{{global:accountAppId}}",
        "tag": "{{input.tag}}"
      }
    mock:
      payload: |
        {
          "tag": "{{input.tag}}",
          "status": "attached"
        }
    output:
      tagResult: "{{response.body}}"

endStage:
  output:
    accountAppId: "{{global:accountAppId}}"
    account: "{{stage:create-account.output.dto}}"
    requestId: "{{global:requestId}}"
  context:
    lastAccountAppId: "{{global:accountAppId}}"
