version: "3.11"
id: "01J9SAMPLELOOKUPNORETRY01"
name: "sample-lookup-no-retry"
description: "Shows a functional lookup that accepts 404 without retrying. Use this pattern for business misses; reserve retry for transient transport failures."
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

stages:
  - name: "lookup-account"
    kind: "Endpoint"
    apiRef: "accounts"
    endpoint: "/api/accounts"
    httpVerb: "GET"
    expectedStatuses: [200, 404]
    headers:
      Authorization: "Bearer {{input.jwt}}"
    query:
      login: "{{input.accountLogin}}"
    onStatus:
      404:
        output:
          lookupMissing: "true"
          lookup: |
            {
              "items": []
            }
    mock:
      status: 404
      payload: |
        {
          "message": "Account not found"
        }
    output:
      lookupStatus: "{{response.status}}"
      lookup: "{{response.body}}"

  - name: "create-account"
    kind: "Endpoint"
    runIf: "{{stage:lookup-account.output.lookupStatus}} == '404' || {{stage:lookup-account.output.lookupMissing}} == 'true'"
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
    mock:
      status: 201
      payload: |
        {
          "appId": "created-001",
          "login": "{{input.accountLogin}}"
        }
    output:
      account: "{{response.body}}"

endStage:
  output:
    lookup: "{{stage:lookup-account.output.lookup}}"
    lookupStatus: "{{stage:lookup-account.output.lookupStatus}}"
    account: "{{stage:create-account.output.account}}"
