version: "1.0.0"
id: "01JQBOOTSTRAPSUBITEM000001"
name: "travelagent-bootstrap-licensing-subscription-item"
description: "Ensures a canonical subscription exists through the admin licensing API using lookup/create/status-transition flow."
output: false
references:
  environmentFile: "../../../../.vscode/.env"
  apis:
    - name: "licensing"
      definition: "LicensingApi"
  workflows:
    - name: "subscription-create-transition"
      path: "{{global:licensingWorkflowDir}}/subscription-create-transition.workflow"
    - name: "subscription-transition-existing"
      path: "{{global:licensingWorkflowDir}}/subscription-transition-existing.workflow"
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
  - name: "subscriptionName"
    type: "Text"
    required: true
  - name: "description"
    type: "Text"
    required: true
  - name: "tierName"
    type: "Text"
    required: true
  - name: "featureCodes"
    type: "Array"
    required: true
  - name: "startDate"
    type: "Text"
    required: true
  - name: "endDate"
    type: "Text"
    required: true
  - name: "capacityAddOns"
    type: "Object"
    required: true
  - name: "tenantId"
    type: "Text"
    required: true
  - name: "targetStatus"
    type: "Text"
    required: true
stages:
  - name: "lookup-tier"
    kind: "Endpoint"
    apiRef: "licensing"
    endpoint: "/api/licensing/tiers"
    httpVerb: "GET"
    expectedStatus: 200
    headers:
      X-Api-Consumer: "{{global:apiConsumerId}}"
      X-Api-Key: "{{global:apiKey}}"
    query:
      q: "name:{{input.tierName}}"
      includeClosed: "false"
    output:
      tierId: "{{response.body.0?.id?}}"
      body: "{{response.body}}"

  - name: "lookup-subscription"
    kind: "Endpoint"
    apiRef: "licensing"
    endpoint: "/api/licensing/subscriptions"
    httpVerb: "GET"
    expectedStatus: 200
    headers:
      X-Api-Consumer: "{{global:apiConsumerId}}"
      X-Api-Key: "{{global:apiKey}}"
    query:
      q: "name:{{input.subscriptionName}} version:1.0"
    output:
      subscriptionId: "{{response.body.0?.id?}}"
      currentStatus: "{{response.body.0?.status?}}"
      body: "{{response.body}}"

  - name: "transition-existing-subscription"
    kind: "Workflow"
    runIf: "!empty({{stage:lookup-subscription.output.subscriptionId}})"
    workflowRef: "subscription-transition-existing"
    inputs:
      subscriptionId: "{{stage:lookup-subscription.output.subscriptionId}}"
      currentStatus: "{{stage:lookup-subscription.output.currentStatus}}"
      targetStatus: "{{input.targetStatus}}"

  - name: "create-and-transition-subscription"
    kind: "Workflow"
    runIf: "empty({{stage:lookup-subscription.output.subscriptionId}})"
    workflowRef: "subscription-create-transition"
    inputs:
      subscriptionName: "{{input.subscriptionName}}"
      description: "{{input.description}}"
      tierId: "{{stage:lookup-tier.output.tierId}}"
      featureCodes: "{{input.featureCodes}}"
      startDate: "{{input.startDate}}"
      endDate: "{{input.endDate}}"
      capacityAddOns: "{{input.capacityAddOns}}"
      tenantId: "{{input.tenantId}}"
      targetStatus: "{{input.targetStatus}}"
endStage:
  output:
    status: "{{input.targetStatus}}"
    subscriptionName: "{{input.subscriptionName}}"
    existingSubscriptionId: "{{stage:lookup-subscription.output.subscriptionId}}"
    existingStatus: "{{stage:lookup-subscription.output.currentStatus}}"
    tierId: "{{stage:lookup-tier.output.tierId}}"
    targetStatus: "{{input.targetStatus}}"
