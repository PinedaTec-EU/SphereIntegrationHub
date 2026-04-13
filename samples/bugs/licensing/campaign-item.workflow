version: "1.0.0"
id: "01JQBOOTSTRAPCAMPITEM0001"
name: "travelagent-bootstrap-licensing-campaign-item"
description: "Ensures a canonical campaign exists through the admin licensing API using lookup, optional draft update, preview/publish, and lifecycle transitions."
output: false
references:
  environmentFile: "../../../../.vscode/.env"
  apis:
    - name: "licensing"
      definition: "LicensingApi"
  workflows:
    - name: "campaign-publish-draft"
      path: "{{global:licensingWorkflowDir}}/campaign-publish-draft.workflow"
    - name: "campaign-transition-existing"
      path: "{{global:licensingWorkflowDir}}/campaign-transition-existing.workflow"
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
  - name: "code"
    type: "Text"
    required: true
  - name: "name"
    type: "Text"
    required: true
  - name: "description"
    type: "Text"
    required: true
  - name: "requestBodyJson"
    type: "Text"
    required: true
  - name: "activationStartDateUtc"
    type: "Text"
    required: true
  - name: "activationEndDateUtc"
    type: "Text"
    required: true
  - name: "usageDurationDaysJson"
    type: "Text"
    required: true
  - name: "maxRedemptionsGlobalJson"
    type: "Text"
    required: true
  - name: "exclusiveGroupCodeJson"
    type: "Text"
    required: true
  - name: "priority"
    type: "Number"
    required: true
  - name: "canStack"
    type: "Text"
    required: true
  - name: "trigger"
    type: "Text"
    required: true
  - name: "requiresCode"
    type: "Text"
    required: true
  - name: "activationCodeJson"
    type: "Text"
    required: true
  - name: "targetStatus"
    type: "Text"
    required: true
  - name: "targetTypeJson"
    type: "Text"
    required: true
  - name: "targetRef"
    type: "Text"
    required: true
  - name: "targetTenantId"
    type: "Text"
    required: true
  - name: "conditionScope"
    type: "Text"
    required: true
  - name: "conditionMatchMode"
    type: "Text"
    required: true
  - name: "requiredFeatureCodes"
    type: "Array"
    required: true
  - name: "providedFeatureCodes"
    type: "Array"
    required: true
  - name: "blockedFeatureCodes"
    type: "Array"
    required: true
  - name: "blockedTierRef"
    type: "Text"
    required: true
  - name: "discountMode"
    type: "Text"
    required: true
  - name: "discountPercentJson"
    type: "Text"
    required: true
  - name: "discountFixedAmountJson"
    type: "Text"
    required: true
  - name: "maxBudgetAmountJson"
    type: "Text"
    required: true
  - name: "currencyJson"
    type: "Text"
    required: true
  - name: "navigatorTierId"
    type: "Text"
    required: true
  - name: "explorerSubscriptionId"
    type: "Text"
    required: true
stages:
  - name: "lookup-campaign"
    kind: "Endpoint"
    apiRef: "licensing"
    endpoint: "/api/licensing/campaigns"
    httpVerb: "GET"
    expectedStatus: 200
    headers:
      X-Api-Consumer: "{{global:apiConsumerId}}"
      X-Api-Key: "{{global:apiKey}}"
    query:
      q: "code:{{input.code}}"
    output:
      campaignId: "{{response.body.0?.id?}}"
      currentStatus: "{{response.body.0?.status?}}"
      body: "{{response.body}}"

  - name: "create-campaign"
    kind: "Endpoint"
    runIf: "empty({{stage:lookup-campaign.output.campaignId}})"
    apiRef: "licensing"
    endpoint: "/api/licensing/campaigns"
    httpVerb: "POST"
    expectedStatus: 201
    headers:
      Content-Type: "application/json"
      X-Api-Consumer: "{{global:apiConsumerId}}"
      X-Api-Key: "{{global:apiKey}}"
    body: |
      {{input.requestBodyJson}}

  - name: "update-draft"
    kind: "Endpoint"
    runIf: '!empty({{stage:lookup-campaign.output.campaignId}}) && {{stage:lookup-campaign.output.currentStatus}} == 1 && {{input.targetStatus}} != "Draft"'
    apiRef: "licensing"
    endpoint: "/api/licensing/campaigns/{{stage:lookup-campaign.output.campaignId}}"
    httpVerb: "PUT"
    expectedStatus: 200
    headers:
      Content-Type: "application/json"
      X-Api-Consumer: "{{global:apiConsumerId}}"
      X-Api-Key: "{{global:apiKey}}"
    body: |
      {{input.requestBodyJson}}

  - name: "lookup-current-draft"
    kind: "Endpoint"
    runIf: "empty({{stage:lookup-campaign.output.campaignId}}) || {{stage:lookup-campaign.output.currentStatus}} == 1"
    apiRef: "licensing"
    endpoint: "/api/licensing/campaigns"
    httpVerb: "GET"
    expectedStatus: 200
    headers:
      X-Api-Consumer: "{{global:apiConsumerId}}"
      X-Api-Key: "{{global:apiKey}}"
    query:
      q: "code:{{input.code}}"
    output:
      campaignId: "{{response.body.0?.id?}}"
      currentStatus: "{{response.body.0?.status?}}"

  - name: "publish-draft"
    kind: "Workflow"
    runIf: '{{input.targetStatus}} != "Draft" && {{stage:lookup-current-draft.output.currentStatus}} == 1'
    workflowRef: "campaign-publish-draft"
    inputs:
      campaignId: "{{stage:lookup-current-draft.output.campaignId}}"
      targetStatus: "{{input.targetStatus}}"

  - name: "transition-existing"
    kind: "Workflow"
    runIf: "!empty({{stage:lookup-campaign.output.campaignId}}) && {{stage:lookup-campaign.output.currentStatus}} != 1"
    workflowRef: "campaign-transition-existing"
    inputs:
      campaignId: "{{stage:lookup-campaign.output.campaignId}}"
      currentStatus: "{{stage:lookup-campaign.output.currentStatus}}"
      targetStatus: "{{input.targetStatus}}"
endStage:
  output:
    code: "{{input.code}}"
    existingCampaignId: "{{stage:lookup-campaign.output.campaignId}}"
    existingStatus: "{{stage:lookup-campaign.output.currentStatus}}"
    targetStatus: "{{input.targetStatus}}"
