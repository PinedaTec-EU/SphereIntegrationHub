version: "3.11"
id: "01JOPENAILLMEXAMPLE000001"
name: "sample-openai-llm"
description: "Demonstrates the OpenAI LLM plugin with prompt resolution, schema output, token limits, timeout, and usage outputs."
output: true
input:
  - name: "openaiApiKey"
    type: "Text"
    required: true
    secret: true
  - name: "customerName"
    type: "Text"
    required: true
  - name: "country"
    type: "Text"
    required: true

stages:
  - name: "prepare-customer-payload"
    kind: "LLM"
    expectedStatus: 200
    config:
      connectionRef: "openai-main"
      model: "gpt-5.4-mini"
      prompts:
        system:
          text: "You transform workflow data into API-ready JSON."
        input:
          text: |
            Build a customer payload using:
            - name: {{input.customerName}}
            - country: {{input.country}}
        output:
          text: "Return only JSON matching the configured schema."
      reasoning:
        effort: "low"
      generation:
        temperature: 0.2
        responseFormat: "schema"
      output:
        schemaName: "customer_payload"
        schemaStrict: true
        schema:
          type: object
          required:
            - name
            - country
          properties:
            name:
              type: string
            country:
              type: string
      limits:
        maxInputTokens: 8000
        maxOutputTokens: 1200
        maxTotalTokens: 9200
        timeoutSeconds: 60
    mock:
      status: 200
      payload: |
        {
          "output": {
            "text": "{\"name\":\"{{input.customerName}}\",\"country\":\"{{input.country}}\"}",
            "json": {
              "name": "{{input.customerName}}",
              "country": "{{input.country}}"
            }
          },
          "usage": {
            "inputTokens": 42,
            "outputTokens": 18,
            "totalTokens": 60,
            "cachedInputTokens": 0,
            "reasoningTokens": 4,
            "model": "gpt-5.4-mini",
            "provider": "openai"
          },
          "finishReason": "completed",
          "durationMs": 100,
          "requestId": "mock-openai-request"
        }
    output:
      payload: "{{response.body.output.text}}"
      inputTokens: "{{response.body.usage.inputTokens}}"
      outputTokens: "{{response.body.usage.outputTokens}}"
      totalTokens: "{{response.body.usage.totalTokens}}"
      reasoningTokens: "{{response.body.usage.reasoningTokens}}"
      finishReason: "{{response.body.finishReason}}"
      requestId: "{{response.body.requestId}}"

endStage:
  output:
    payload: "{{stage:prepare-customer-payload.output.payload}}"
    inputTokens: "{{stage:prepare-customer-payload.output.inputTokens}}"
    outputTokens: "{{stage:prepare-customer-payload.output.outputTokens}}"
    totalTokens: "{{stage:prepare-customer-payload.output.totalTokens}}"
    reasoningTokens: "{{stage:prepare-customer-payload.output.reasoningTokens}}"
    finishReason: "{{stage:prepare-customer-payload.output.finishReason}}"
    requestId: "{{stage:prepare-customer-payload.output.requestId}}"
