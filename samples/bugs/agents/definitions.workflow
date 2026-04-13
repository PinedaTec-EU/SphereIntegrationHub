version: "1.0.0"
id: "01JQBOOTSTRAPAGENTS000001"
name: "travelagent-bootstrap-suite-agents"
description: "Seeds suite AI agent definitions through the Admin API import endpoint."
output: false
references:
  environmentFile: "../../../../.vscode/.env"
  apis:
    - name: "admin"
      definition: "AdminApi"
initStage:
  variables:
    - name: "agentsDataDir"
      type: "Fixed"
      value: "./data"
    - name: "suiteAgentsImportBodyFile"
      type: "Fixed"
      value: "{{global:agentsDataDir}}/suite-agents-import.json"
    - name: "apiConsumerId"
      type: "Fixed"
      value: "{{env:ApiSecurityBootstrap__System__ConsumerId}}"
      secret: true
    - name: "apiKey"
      type: "Fixed"
      value: "{{env:ApiSecurityBootstrap__System__ApiKey}}"
      secret: true
stages:
  - name: "import-suite-agents"
    kind: "Endpoint"
    apiRef: "admin"
    endpoint: "/api/suite-agents/import"
    httpVerb: "POST"
    expectedStatus: 200
    headers:
      Content-Type: "application/json"
      X-Api-Consumer: "{{global:apiConsumerId}}"
      X-Api-Key: "{{global:apiKey}}"
    bodyFile: "{{global:suiteAgentsImportBodyFile}}"
    output:
      body: "{{response.body}}"

endStage:
  output:
    status: "Imported"
    summary: "Suite AI agent definitions were imported through Admin API."
    result: "{{stage:import-suite-agents.output.body}}"
