version: "1.0.0"
id: "01JQBOOTSTRAPFAREITEM00001"
name: "travelagent-bootstrap-licensing-fare-item"
description: "Ensures a canonical fare exists through the admin licensing API using the existing CRUD and lifecycle endpoints."
output: false
references:
  environmentFile: "../../../../.vscode/.env"
  apis:
    - name: "licensing"
      definition: "LicensingApi"
  workflows:
    - name: "fare-transition-existing"
      path: "{{global:licensingWorkflowDir}}/fare-transition-existing.workflow"
    - name: "fare-update-existing-draft"
      path: "{{global:licensingWorkflowDir}}/fare-update-existing-draft.workflow"
initStage:
  variables:
    - name: "licensingWorkflowDir"
      type: "Fixed"
      value: "."
    - name: "apiConsumerId"
      type: "Fixed"
      value: "{{env:ApiSecurityBootstrap__System__ConsumerId}}"
      secret: true
    - name: "apiKey"
      type: "Fixed"
      value: "{{env:ApiSecurityBootstrap__System__ApiKey}}"
      secret: true
input:
  - name: "fareName"
    type: "Text"
    required: true
  - name: "lookupQuery"
    type: "Text"
    required: true
  - name: "createdLookupQuery"
    type: "Text"
    required: true
  - name: "description"
    type: "Text"
    required: true
  - name: "activationStartDate"
    type: "Text"
    required: true
  - name: "activationEndDate"
    type: "Text"
    required: true
  - name: "priceLinesJson"
    type: "Text"
    required: true
  - name: "allowCapacityAddOnsJson"
    type: "Text"
    required: true
  - name: "capacityAddOnPricingJson"
    type: "Text"
    required: true
  - name: "referralPolicyJson"
    type: "Text"
    required: true
  - name: "currency"
    type: "Text"
    required: true
  - name: "targetStatus"
    type: "Text"
    required: true
stages:
  - name: "lookup-fare"
    kind: "Endpoint"
    apiRef: "licensing"
    endpoint: "/api/licensing/fares"
    httpVerb: "GET"
    expectedStatus: 200
    headers:
      X-Api-Consumer: "{{global:apiConsumerId}}"
      X-Api-Key: "{{global:apiKey}}"
    query:
      q: "{{input.lookupQuery}}"
    output:
      fareId: "{{response.body.0?.id?}}"
      currentStatus: "{{response.body.0?.status?}}"
      body: "{{response.body}}"

  - name: "create-fare"
    kind: "Endpoint"
    runIf: "empty({{stage:lookup-fare.output.fareId}})"
    apiRef: "licensing"
    endpoint: "/api/licensing/fares"
    httpVerb: "POST"
    expectedStatus: 201
    headers:
      Content-Type: "application/json"
      X-Api-Consumer: "{{global:apiConsumerId}}"
      X-Api-Key: "{{global:apiKey}}"
    body: |
      {
        "name": "{{input.fareName}}",
        "description": "{{input.description}}",
        "activationStartDate": "{{input.activationStartDate}}",
        "activationEndDate": "{{input.activationEndDate}}",
        "priceLines": {{input.priceLinesJson}},
        "referralPolicy": {{input.referralPolicyJson}},
        "currency": "{{input.currency}}",
        "allowCapacityAddOns": {{input.allowCapacityAddOnsJson}},
        "capacityAddOnPricing": {{input.capacityAddOnPricingJson}}
      }
    output:
      fareId: "{{response.body.id}}"
      body: "{{response.body}}"

  - name: "lookup-created-fare"
    kind: "Endpoint"
    runIf: "empty({{stage:lookup-fare.output.fareId}})"
    apiRef: "licensing"
    endpoint: "/api/licensing/fares"
    httpVerb: "GET"
    expectedStatus: 200
    headers:
      X-Api-Consumer: "{{global:apiConsumerId}}"
      X-Api-Key: "{{global:apiKey}}"
    query:
      q: "{{input.createdLookupQuery}}"
    output:
      fareId: "{{response.body.0?.id?}}"
      currentStatus: "{{response.body.0?.status?}}"

  - name: "update-existing-draft"
    kind: "Workflow"
    runIf: "{{stage:lookup-fare.output.currentStatus}} == 1"
    workflowRef: "fare-update-existing-draft"
    inputs:
      fareId: "{{stage:lookup-fare.output.fareId}}"
      fareName: "{{input.fareName}}"
      description: "{{input.description}}"
      activationStartDate: "{{input.activationStartDate}}"
      activationEndDate: "{{input.activationEndDate}}"
      priceLinesJson: "{{input.priceLinesJson}}"
      allowCapacityAddOnsJson: "{{input.allowCapacityAddOnsJson}}"
      capacityAddOnPricingJson: "{{input.capacityAddOnPricingJson}}"
      referralPolicyJson: "{{input.referralPolicyJson}}"
      currency: "{{input.currency}}"
      targetStatus: "{{input.targetStatus}}"

  - name: "transition-created-fare"
    kind: "Workflow"
    runIf: "empty({{stage:lookup-fare.output.fareId}})"
    workflowRef: "fare-transition-existing"
    inputs:
      fareId: "{{stage:lookup-created-fare.output.fareId}}"
      currentStatus: "1"
      targetStatus: "{{input.targetStatus}}"

  - name: "transition-existing-fare"
    kind: "Workflow"
    runIf: "!empty({{stage:lookup-fare.output.fareId}})"
    workflowRef: "fare-transition-existing"
    inputs:
      fareId: "{{stage:lookup-fare.output.fareId}}"
      currentStatus: "{{stage:lookup-fare.output.currentStatus}}"
      targetStatus: "{{input.targetStatus}}"
endStage:
  output:
    fareName: "{{input.fareName}}"
    fareId: "{{stage:lookup-fare.output.fareId}}"
    currentStatus: "{{stage:lookup-fare.output.currentStatus}}"
    targetStatus: "{{input.targetStatus}}"
