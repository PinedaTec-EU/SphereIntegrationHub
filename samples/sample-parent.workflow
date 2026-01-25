version: "3.11"
id: "01J9SAMPLEPARENT0000000001"
name: "sample-parent"
description: "Looks up an account, calls the child workflow, and registers metadata."
output: true
references:
  workflows:
    - name: "sample-child"
      path: "./sample-child.workflow"
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
  - name: "requestedBy"
    type: "Text"
    required: true
initStage:
  context:
    requestedBy: "{{input.requestedBy}}"

resilience:
  retries:
    standard:
      maxRetries: 3
      delayMs: 250
  circuitBreakers:
    metadata:
      failureThreshold: 5
      breakMs: 30000

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
          "items": [
            {
              "appId": "existing-001",
              "login": "{{input.accountLogin}}"
            }
          ]
        }
    output:
      lookup: "{{response.body}}"
    message: "Lookup status {{response.status}} for {{input.accountLogin}}."
    retry:
      ref: "standard"
      httpStatus: [500, 503]
      messages:
        onException: "Lookup fallo tras reintentos."

  - name: "create-account"
    kind: "Workflow"
    # Workflow stages do not support retry/circuitBreaker.
    workflowRef: "sample-child"
    inputs:
      jwt: "{{input.jwt}}"
      accountLogin: "{{input.accountLogin}}"
      accountPassword: "{{input.accountPassword}}"
      tag: "{{input.tag}}"
    mock:
      output:
        accountAppId: "mocked-account-app-id"
        account: |
          {"appId":"mocked-account-app-id","login":"{{input.accountLogin}}"}
        requestId: "01J9MOCKREQ0000000000001"

  - name: "register-metadata"
    kind: "Endpoint"
    runIf: "{{stage:create-account.output.accountAppId}} != null"
    apiRef: "accounts"
    endpoint: "/api/accounts/{{stage:create-account.output.accountAppId}}/metadata"
    httpVerb: "POST"
    expectedStatus: 200
    headers:
      Content-Type: "application/json"
      Authorization: "Bearer {{input.jwt}}"
    body: |
      {
        "requestedBy": "{{context.requestedBy}}",
        "requestId": "{{stage:create-account.output.requestId}}"
      }
    retry:
      ref: "standard"
      httpStatus: [500, 503]
    circuitBreaker:
      ref: "metadata"
      messages:
        onOpen: "Circuit abierto en metadata."
        onBlocked: "Circuit abierto. Se omite metadata."
    mock:
      payload: |
        {
          "status": "registered"
        }
    output:
      metadataStatus: "{{response.body}}"

endStage:
  output:
    accountAppId: "{{stage:create-account.output.accountAppId}}"
    account: "{{stage:create-account.output.account}}"
    lookup: "{{stage:lookup-account.output.lookup}}"
    metadataStatus: "{{stage:register-metadata.output.metadataStatus}}"
  context:
    lastAccountAppId: "{{stage:create-account.output.accountAppId}}"
