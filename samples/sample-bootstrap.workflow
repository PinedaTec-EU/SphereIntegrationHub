version: "3.11"
id: "01J9SAMPLEBOOTSTRAP0000001"
name: "sample-bootstrap-seed"
description: "Demonstrates ensure, expectedStatuses, onStatus, bodyFile, dataFile, and forEach for collection bootstraps."
output: true
references:
  apis:
    - name: "accounts"
      definition: "accounts"
input:
  - name: "jwt"
    type: "Text"
    required: true

stages:
  - name: "seed-accounts"
    kind: "Http"
    expectedStatuses: [201, 409]
    dataFile: "./seed/accounts.json"
    forEach: "accounts"
    itemName: "account"
    indexName: "accountIndex"
    config:
      apiRef: "accounts"
      endpoint: "/api/accounts"
      httpVerb: "POST"
      bodyFile: "./payloads/bootstrap-account.json"
      headers:
        Content-Type: "application/json"
        Authorization: "Bearer {{input.jwt}}"
    onStatus:
      409:
        output:
          conflict: "true"
          conflictBody: "{{response.body}}"
    ensure:
      mode: "CreateIfMissing"
      existsOn: [409]
      output:
        seedStatus: "existing"
    mock:
      status: 201
      payload: |
        {
          "appId": "seeded-{{context:accountIndex}}",
          "login": "{{context:account.login}}"
        }
    output:
      created: "{{response.body}}"
      accountAppId: "{{response.body.appId?}}"

endStage:
  output:
    foreachCount: "{{stage:seed-accounts.output.foreach_count}}"
    foreachItems: "{{stage:seed-accounts.output.foreach_items}}"
