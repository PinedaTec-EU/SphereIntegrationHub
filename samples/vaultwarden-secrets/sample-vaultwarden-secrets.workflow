version: "3.11"
id: "01JVAULTWARDENSAMPLE000001"
name: "sample-vaultwarden-secrets"
description: "Loads secrets from the vaultwarden provider into env tokens and uses them in an HTTP stage."
output: true
references:
  apis:
    - name: "accounts"
      definition: "accounts"
stages:
  - name: "list-accounts"
    kind: "Http"
    expectedStatus: 200
    config:
      apiRef: "accounts"
      endpoint: "/api/accounts"
      httpVerb: "GET"
      headers:
        Authorization: "Bearer {{env:ACCOUNTS_API_TOKEN}}"
        X-Client-Secret: "{{env:ACCOUNTS_CLIENT_SECRET}}"
    output:
      http_status: "{{response.status}}"

endStage:
  output:
    status: "{{stage:list-accounts.output.http_status}}"
