version: "1.0.0"
id: "01JQBOOTSTRAPFARES0000001"
name: "travelagent-bootstrap-licensing-fares"
description: "Ensures canonical fares exist through the Admin Licensing API."
output: false
references:
  environmentFile: "../../../../.vscode/.env"
  apis:
    - name: "licensing"
      definition: "LicensingApi"
  workflows:
    - name: "fare-item"
      path: "{{global:licensingWorkflowDir}}/fare-item.workflow"
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
stages:
  - name: "lookup-essential"
    kind: "Endpoint"
    apiRef: "licensing"
    endpoint: "/api/licensing/tiers"
    httpVerb: "GET"
    expectedStatus: 200
    headers:
      X-Api-Consumer: "{{global:apiConsumerId}}"
      X-Api-Key: "{{global:apiKey}}"
    query:
      q: "name:Essential"
      includeClosed: "false"
    output:
      tierId: "{{response.body.0?.id?}}"

  - name: "lookup-traveller"
    kind: "Endpoint"
    apiRef: "licensing"
    endpoint: "/api/licensing/tiers"
    httpVerb: "GET"
    expectedStatus: 200
    headers:
      X-Api-Consumer: "{{global:apiConsumerId}}"
      X-Api-Key: "{{global:apiKey}}"
    query:
      q: "name:Traveller"
      includeClosed: "false"
    output:
      tierId: "{{response.body.0?.id?}}"

  - name: "lookup-explorer"
    kind: "Endpoint"
    apiRef: "licensing"
    endpoint: "/api/licensing/tiers"
    httpVerb: "GET"
    expectedStatus: 200
    headers:
      X-Api-Consumer: "{{global:apiConsumerId}}"
      X-Api-Key: "{{global:apiKey}}"
    query:
      q: "name:Explorer"
      includeClosed: "false"
    output:
      tierId: "{{response.body.0?.id?}}"

  - name: "lookup-navigator"
    kind: "Endpoint"
    apiRef: "licensing"
    endpoint: "/api/licensing/tiers"
    httpVerb: "GET"
    expectedStatus: 200
    headers:
      X-Api-Consumer: "{{global:apiConsumerId}}"
      X-Api-Key: "{{global:apiKey}}"
    query:
      q: "name:Navigator"
      includeClosed: "false"
    output:
      tierId: "{{response.body.0?.id?}}"

  - name: "lookup-voyager"
    kind: "Endpoint"
    apiRef: "licensing"
    endpoint: "/api/licensing/tiers"
    httpVerb: "GET"
    expectedStatus: 200
    headers:
      X-Api-Consumer: "{{global:apiConsumerId}}"
      X-Api-Key: "{{global:apiKey}}"
    query:
      q: "name:Voyager"
      includeClosed: "false"
    output:
      tierId: "{{response.body.0?.id?}}"

  - name: "ensure-2025-standard-closed"
    kind: "Workflow"
    workflowRef: "fare-item"
    inputs:
      fareName: "2025 Standard Pricing"
      lookupQuery: "name:2025 Standard Pricing status:closed"
      createdLookupQuery: "name:2025 Standard Pricing status:draft"
      description: "Standard pricing model for 2025 with capacity add-on packages."
      activationStartDate: "2025-01-01"
      activationEndDate: "2025-12-31"
      priceLinesJson: |
        [
          {"tierOrFeatureId":"{{stage:lookup-traveller.output.tierId}}","tierOrFeatureName":"Traveller","isFeature":false,"price":19.99},
          {"tierOrFeatureId":"{{stage:lookup-explorer.output.tierId}}","tierOrFeatureName":"Explorer","isFeature":false,"price":49.99},
          {"tierOrFeatureId":"{{stage:lookup-navigator.output.tierId}}","tierOrFeatureName":"Navigator","isFeature":false,"price":99.99},
          {"tierOrFeatureId":"{{stage:lookup-voyager.output.tierId}}","tierOrFeatureName":"Voyager","isFeature":false,"price":199.99}
        ]
      allowCapacityAddOnsJson: |
        {"PropertyLimit":true,"TeamMembers":true,"Triggers":false,"ChatBot":false}
      capacityAddOnPricingJson: |
        {"PropertyLimit":{"10":15.00,"25":30.00,"100":100.00},"TeamMembers":{"5":10.00,"10":18.00,"25":40.00}}
      referralPolicyJson: |
        {"sourceReferrerBenefit":{"mode":2,"discountPercent":10.0,"maxDiscountAmount":100.0,"currency":"EUR"},"destinationRefereeBenefit":{"mode":3,"discountFixedAmount":20.0,"currency":"EUR"}}
      currency: "EUR"
      targetStatus: "Closed"

  - name: "ensure-2025-traveller-no-addons-draft"
    kind: "Workflow"
    workflowRef: "fare-item"
    inputs:
      fareName: "2025 Traveller Tier (No Add-ons)"
      lookupQuery: "name:2025 Traveller Tier status:draft"
      createdLookupQuery: "name:2025 Traveller Tier status:draft"
      description: "Special pricing for Free/Traveller tier without capacity add-ons."
      activationStartDate: "2025-01-01"
      activationEndDate: "2025-12-31"
      priceLinesJson: |
        [
          {"tierOrFeatureId":"{{stage:lookup-traveller.output.tierId}}","tierOrFeatureName":"Traveller","isFeature":false,"price":19.99}
        ]
      allowCapacityAddOnsJson: |
        {"PropertyLimit":false,"TeamMembers":false,"Triggers":false,"ChatBot":false}
      capacityAddOnPricingJson: |
        {}
      referralPolicyJson: |
        {"sourceReferrerBenefit":{"mode":1,"currency":null},"destinationRefereeBenefit":{"mode":1,"currency":null}}
      currency: "EUR"
      targetStatus: "Draft"

  - name: "ensure-2026-commercial-active"
    kind: "Workflow"
    workflowRef: "fare-item"
    inputs:
      fareName: "2026 Unified Commercial Pricing"
      lookupQuery: "name:2026 Unified Commercial Pricing status:active"
      createdLookupQuery: "name:2026 Unified Commercial Pricing status:draft"
      description: "Primary active fare for 2026 with full catalog coverage across tiers, all feature categories, and referral incentives."
      activationStartDate: "2026-01-01"
      activationEndDate: "2026-12-31"
      priceLinesJson: |
        [
          {"tierOrFeatureId":"{{stage:lookup-essential.output.tierId}}","tierOrFeatureName":"Essential","isFeature":false,"price":12.99},
          {"tierOrFeatureId":"{{stage:lookup-traveller.output.tierId}}","tierOrFeatureName":"Traveller","isFeature":false,"price":24.99},
          {"tierOrFeatureId":"{{stage:lookup-explorer.output.tierId}}","tierOrFeatureName":"Explorer","isFeature":false,"price":59.99},
          {"tierOrFeatureId":"{{stage:lookup-navigator.output.tierId}}","tierOrFeatureName":"Navigator","isFeature":false,"price":119.99},
          {"tierOrFeatureId":"{{stage:lookup-voyager.output.tierId}}","tierOrFeatureName":"Voyager","isFeature":false,"price":239.99},
          {"tierOrFeatureId":"TRG01","tierOrFeatureName":"All Triggers","isFeature":true,"price":9.75},
          {"tierOrFeatureId":"TRG02","tierOrFeatureName":"On Tenant Registered","isFeature":true,"price":8.13},
          {"tierOrFeatureId":"TRG03","tierOrFeatureName":"On Customer Arrival Day","isFeature":true,"price":7.48},
          {"tierOrFeatureId":"TRG04","tierOrFeatureName":"On Customer Checkout Day","isFeature":true,"price":7.15},
          {"tierOrFeatureId":"TRG05","tierOrFeatureName":"On Travel Date Is Close","isFeature":true,"price":6.83},
          {"tierOrFeatureId":"TRG06","tierOrFeatureName":"On Travel Last Day","isFeature":true,"price":6.50},
          {"tierOrFeatureId":"TRG07","tierOrFeatureName":"On Booking Created","isFeature":true,"price":6.50},
          {"tierOrFeatureId":"TRG08","tierOrFeatureName":"On Booking Cancelled","isFeature":true,"price":6.50},
          {"tierOrFeatureId":"BOT01","tierOrFeatureName":"All ChatBot Channels","isFeature":true,"price":11.25},
          {"tierOrFeatureId":"BOT02","tierOrFeatureName":"WhatsApp Integration","isFeature":true,"price":9.38},
          {"tierOrFeatureId":"BOT03","tierOrFeatureName":"Telegram Integration","isFeature":true,"price":8.63},
          {"tierOrFeatureId":"BOT04","tierOrFeatureName":"Email Integration","isFeature":true,"price":8.25},
          {"tierOrFeatureId":"BOT05","tierOrFeatureName":"Website Integration","isFeature":true,"price":7.88},
          {"tierOrFeatureId":"BOT06","tierOrFeatureName":"Bidirectional Translation (Ollama)","isFeature":true,"price":7.50},
          {"tierOrFeatureId":"BOT07","tierOrFeatureName":"Bidirectional Translation (Cloud LLM APIs)","isFeature":true,"price":7.50},
          {"tierOrFeatureId":"TEN01","tierOrFeatureName":"All Personalization Features","isFeature":true,"price":8.25},
          {"tierOrFeatureId":"TEN02","tierOrFeatureName":"Brand Colors Customization","isFeature":true,"price":6.88},
          {"tierOrFeatureId":"TEN03","tierOrFeatureName":"Custom Message Templates","isFeature":true,"price":6.32},
          {"tierOrFeatureId":"TEN04","tierOrFeatureName":"Custom Logo Upload","isFeature":true,"price":6.05},
          {"tierOrFeatureId":"TEN05","tierOrFeatureName":"Custom Domain Configuration","isFeature":true,"price":5.78},
          {"tierOrFeatureId":"TEN06","tierOrFeatureName":"Late checkout support","isFeature":true,"price":5.50},
          {"tierOrFeatureId":"TEN07","tierOrFeatureName":"Review post-stay support","isFeature":true,"price":5.50},
          {"tierOrFeatureId":"TEN08","tierOrFeatureName":"Shuttle Service Support","isFeature":true,"price":5.65},
          {"tierOrFeatureId":"API01","tierOrFeatureName":"API calls","isFeature":true,"price":10.13},
          {"tierOrFeatureId":"PAY01","tierOrFeatureName":"Payment Integration Standard","isFeature":true,"price":11.25},
          {"tierOrFeatureId":"PAY02","tierOrFeatureName":"Payment Gateway (Roadmap)","isFeature":true,"price":9.38},
          {"tierOrFeatureId":"REVN01","tierOrFeatureName":"Revenue Basic","isFeature":true,"price":9.00},
          {"tierOrFeatureId":"REVN02","tierOrFeatureName":"Revenue Premium","isFeature":true,"price":7.50},
          {"tierOrFeatureId":"REVN03","tierOrFeatureName":"Airport Transfers","isFeature":true,"price":6.90},
          {"tierOrFeatureId":"REVN04","tierOrFeatureName":"Breakfast Upsells","isFeature":true,"price":6.60},
          {"tierOrFeatureId":"REVN05","tierOrFeatureName":"Local Activities Information For Property","isFeature":true,"price":6.30},
          {"tierOrFeatureId":"USG01","tierOrFeatureName":"Usage Metering Basic","isFeature":true,"price":7.88},
          {"tierOrFeatureId":"USG02","tierOrFeatureName":"Usage Metering Premium","isFeature":true,"price":6.56},
          {"tierOrFeatureId":"INT01","tierOrFeatureName":"Integracion Sede Electronica Policia Nacional (ES)","isFeature":true,"price":13.50},
          {"tierOrFeatureId":"INT02","tierOrFeatureName":"Digital Contract Signature","isFeature":true,"price":11.25},
          {"tierOrFeatureId":"INT03","tierOrFeatureName":"PMS and OTA Integrations","isFeature":true,"price":10.35},
          {"tierOrFeatureId":"REV01","tierOrFeatureName":"Review Aggregation","isFeature":true,"price":6.75},
          {"tierOrFeatureId":"REV02","tierOrFeatureName":"Public Reviews Portal","isFeature":true,"price":7.50},
          {"tierOrFeatureId":"REV03","tierOrFeatureName":"Review Widget","isFeature":true,"price":5.18},
          {"tierOrFeatureId":"REV04","tierOrFeatureName":"Review Analytics","isFeature":true,"price":6.60}
        ]
      allowCapacityAddOnsJson: |
        {"PropertyLimit":true,"TeamMembers":true,"Triggers":true,"ChatBot":true}
      capacityAddOnPricingJson: |
        {"PropertyLimit":{"10":20.00,"25":38.00,"100":130.00},"TeamMembers":{"5":14.00,"10":25.00,"25":54.00},"Triggers":{"200":27.00,"500":52.00},"ChatBot":{"100":22.00,"500":85.00}}
      referralPolicyJson: |
        {"sourceReferrerBenefit":{"mode":2,"discountPercent":10.0,"maxDiscountAmount":100.0,"currency":"EUR"},"destinationRefereeBenefit":{"mode":3,"discountFixedAmount":25.0,"currency":"EUR"}}
      currency: "EUR"
      targetStatus: "Active"

  - name: "ensure-2027-advance-pricing-scheduled"
    kind: "Workflow"
    workflowRef: "fare-item"
    inputs:
      fareName: "2027 Advance Pricing (Scheduled)"
      lookupQuery: "name:2027 Advance Pricing status:scheduled"
      createdLookupQuery: "name:2027 Advance Pricing status:draft"
      description: "Premium pricing structure scheduled as a future debug case with expanded add-on options."
      activationStartDate: "2027-01-01"
      activationEndDate: "2027-12-31"
      priceLinesJson: |
        [
          {"tierOrFeatureId":"{{stage:lookup-traveller.output.tierId}}","tierOrFeatureName":"Traveller","isFeature":false,"price":24.99},
          {"tierOrFeatureId":"{{stage:lookup-explorer.output.tierId}}","tierOrFeatureName":"Explorer","isFeature":false,"price":59.99},
          {"tierOrFeatureId":"{{stage:lookup-navigator.output.tierId}}","tierOrFeatureName":"Navigator","isFeature":false,"price":119.99},
          {"tierOrFeatureId":"{{stage:lookup-voyager.output.tierId}}","tierOrFeatureName":"Voyager","isFeature":false,"price":249.99}
        ]
      allowCapacityAddOnsJson: |
        {"PropertyLimit":true,"TeamMembers":true,"Triggers":true,"ChatBot":true}
      capacityAddOnPricingJson: |
        {"PropertyLimit":{"10":18.00,"25":35.00,"100":120.00},"TeamMembers":{"5":12.00,"10":22.00,"25":50.00},"Triggers":{"200":25.00,"500":50.00},"ChatBot":{"100":20.00,"500":80.00}}
      referralPolicyJson: |
        {"sourceReferrerBenefit":{"mode":1,"currency":null},"destinationRefereeBenefit":{"mode":1,"currency":null}}
      currency: "EUR"
      targetStatus: "Scheduled"

  - name: "ensure-2026-q4-draft"
    kind: "Workflow"
    workflowRef: "fare-item"
    inputs:
      fareName: "2026 Q4 Draft Pricing"
      lookupQuery: "name:2026 Q4 Draft status:draft"
      createdLookupQuery: "name:2026 Q4 Draft status:draft"
      description: "Draft pricing candidate for Q4 2026 seasonal adjustments."
      activationStartDate: "2026-10-01"
      activationEndDate: "2026-12-31"
      priceLinesJson: |
        [
          {"tierOrFeatureId":"{{stage:lookup-traveller.output.tierId}}","tierOrFeatureName":"Traveller","isFeature":false,"price":26.99},
          {"tierOrFeatureId":"{{stage:lookup-explorer.output.tierId}}","tierOrFeatureName":"Explorer","isFeature":false,"price":62.99}
        ]
      allowCapacityAddOnsJson: |
        {"PropertyLimit":true,"TeamMembers":true,"Triggers":true,"ChatBot":false}
      capacityAddOnPricingJson: |
        {"PropertyLimit":{"10":19.00,"25":36.00},"TeamMembers":{"5":13.00,"10":23.00},"Triggers":{"200":26.00}}
      referralPolicyJson: |
        {"sourceReferrerBenefit":{"mode":1,"currency":null},"destinationRefereeBenefit":{"mode":1,"currency":null}}
      currency: "EUR"
      targetStatus: "Draft"

  - name: "ensure-2024-legacy-closed"
    kind: "Workflow"
    workflowRef: "fare-item"
    inputs:
      fareName: "2024 Legacy Pricing (Closed)"
      lookupQuery: "name:2024 Legacy Pricing status:closed"
      createdLookupQuery: "name:2024 Legacy Pricing status:draft"
      description: "Legacy pricing model retired after 2024."
      activationStartDate: "2024-01-01"
      activationEndDate: "2024-12-31"
      priceLinesJson: |
        [
          {"tierOrFeatureId":"{{stage:lookup-traveller.output.tierId}}","tierOrFeatureName":"Traveller","isFeature":false,"price":17.99},
          {"tierOrFeatureId":"{{stage:lookup-explorer.output.tierId}}","tierOrFeatureName":"Explorer","isFeature":false,"price":44.99}
        ]
      allowCapacityAddOnsJson: |
        {"PropertyLimit":true,"TeamMembers":false,"Triggers":false,"ChatBot":false}
      capacityAddOnPricingJson: |
        {"PropertyLimit":{"10":12.00,"25":24.00}}
      referralPolicyJson: |
        {"sourceReferrerBenefit":{"mode":1,"currency":null},"destinationRefereeBenefit":{"mode":1,"currency":null}}
      currency: "EUR"
      targetStatus: "Closed"

endStage:
  output:
    status: "Processed"
    faresStatus2025Standard: "{{stage:ensure-2025-standard-closed.output.targetStatus}}"
    faresStatus2025TravellerNoAddons: "{{stage:ensure-2025-traveller-no-addons-draft.output.targetStatus}}"
    faresStatus2026Commercial: "{{stage:ensure-2026-commercial-active.output.targetStatus}}"
    faresStatus2027Advance: "{{stage:ensure-2027-advance-pricing-scheduled.output.targetStatus}}"
    faresStatus2026Q4Draft: "{{stage:ensure-2026-q4-draft.output.targetStatus}}"
    faresStatus2024Legacy: "{{stage:ensure-2024-legacy-closed.output.targetStatus}}"
    summary: "Licensing canonical fares bootstrap executed through Admin Licensing API."
