version: "1.0.0"
id: "01JQBOOTSTRAPGCRADMINGLB001"
name: "travelagent-bootstrap-gcr-admin-global"
description: "Seeds admin/global GCR configuration through the GCR API."
output: false
references:
  environmentFile: "../../../../.vscode/.env"
  apis:
    - name: "gcr"
      definition: "GcrApi"
initStage:
  variables:
    - name: "gcrDataDir"
      type: "Fixed"
      value: "./data"
    - name: "adminGlobalBodyFile"
      type: "Fixed"
      value: "{{global:gcrDataDir}}/admin-global.body.json"
    - name: "apiConsumerId"
      type: "Fixed"
      value: "system"
      secret: true
    - name: "apiKey"
      type: "Fixed"
      value: "{{env:GcrBootstrap__InitializationKey}}"
      secret: true
stages:
  - name: "upsert-admin-global"
    kind: "Endpoint"
    apiRef: "gcr"
    endpoint: "/api/gcr/configurations/admin/global"
    httpVerb: "PUT"
    expectedStatus: 200
    headers:
      Content-Type: "application/json"
      X-Api-Consumer: "{{global:apiConsumerId}}"
      X-Api-Key: "{{global:apiKey}}"
    bodyFile: "{{global:adminGlobalBodyFile}}"
    output:
      body: "{{response.body}}"
endStage:
  output:
    summary: "Admin global GCR configuration upserted."
    result: "{{stage:upsert-admin-global.output.body}}"
