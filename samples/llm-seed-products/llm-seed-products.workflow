version: "3.11"
id: "01JLLMSEEDPRODUCTS0000001"
name: "llm-seed-products"
description: |
  Uses the OpenAI LLM plugin to generate 5 fictional product records and then
  seeds each one into the catalog API via a forEach loop.
  Demonstrates LLM-generated structured data feeding a REST write endpoint.
output: true
references:
  apis:
    - name: catalog
      definition: catalog

input:
  - name: openaiApiKey
    type: Text
    required: true
    secret: true
  - name: jwt
    type: Text
    required: true
    secret: true
  - name: category
    type: Text
    required: false
    description: "Product category the LLM will generate items for."

stages:
  - name: generate-products
    kind: LLM
    expectedStatus: 200
    config:
      connectionRef: openai-main
      model: gpt-4o-mini
      prompts:
        system:
          text: >
            You are a product catalog generator.
            Return only valid JSON. No markdown fences, no extra commentary.
        input:
          text: |
            Generate exactly 5 fictional product records for the category: {{input.category}}.
            Each product must have:
            - a short, realistic name
            - a unique uppercase SKU (e.g. WCP-001)
            - a price in EUR as a decimal number
            - the category value passed above
            - a one-sentence description (max 120 characters)
        output:
          text: "Return only JSON matching the configured schema."
      generation:
        temperature: 0.8
        responseFormat: schema
      output:
        schemaName: product_list
        schemaStrict: true
        schema:
          type: object
          required:
            - products
          properties:
            products:
              type: array
              minItems: 5
              maxItems: 5
              items:
                type: object
                required:
                  - name
                  - sku
                  - price
                  - category
                  - description
                properties:
                  name:
                    type: string
                  sku:
                    type: string
                  price:
                    type: number
                  category:
                    type: string
                  description:
                    type: string
      limits:
        maxOutputTokens: 800
        timeoutSeconds: 45
    mock:
      status: 200
      payload: |
        {
          "output": {
            "json": {
              "products": [
                {
                  "name": "Wireless Charging Pad Pro",
                  "sku": "WCP-001",
                  "price": 34.99,
                  "category": "Tech accessories",
                  "description": "Fast 15W wireless charger compatible with all Qi-enabled devices."
                },
                {
                  "name": "USB-C Hub 7-in-1",
                  "sku": "HUB-007",
                  "price": 49.95,
                  "category": "Tech accessories",
                  "description": "Compact hub with HDMI 4K, 3x USB-A, SD reader, and 100W PD pass-through."
                },
                {
                  "name": "Laptop Privacy Screen 15in",
                  "sku": "SCR-015",
                  "price": 27.50,
                  "category": "Tech accessories",
                  "description": "Anti-glare privacy filter that blocks side-angle viewing up to 30 degrees."
                },
                {
                  "name": "Mechanical Keycap Set",
                  "sku": "KCP-RGB",
                  "price": 19.90,
                  "category": "Tech accessories",
                  "description": "104-key PBT double-shot keycaps with RGB shine-through legends."
                },
                {
                  "name": "Portable SSD 1TB",
                  "sku": "SSD-1TB",
                  "price": 89.00,
                  "category": "Tech accessories",
                  "description": "Rugged USB 3.2 Gen 2 drive with 1050 MB/s read speed and IP55 rating."
                }
              ]
            }
          },
          "usage": {
            "inputTokens": 115,
            "outputTokens": 275,
            "totalTokens": 390,
            "model": "gpt-4o-mini",
            "provider": "openai"
          },
          "finishReason": "completed",
          "durationMs": 1850,
          "requestId": "mock-llm-seed-001"
        }
    output:
      products: "{{response.body.output.json.products}}"
      inputTokens: "{{response.body.usage.inputTokens}}"
      outputTokens: "{{response.body.usage.outputTokens}}"
      totalTokens: "{{response.body.usage.totalTokens}}"
      finishReason: "{{response.body.finishReason}}"

  - name: create-products
    kind: Endpoint
    apiRef: catalog
    endpoint: /api/products
    httpVerb: POST
    expectedStatuses: [200, 201]
    forEach: "{{stage:generate-products.output.products}}"
    itemName: product
    indexName: productIndex
    headers:
      Content-Type: application/json
      Authorization: Bearer {{input.jwt}}
    body: |
      {
        "name": "{{context:product.name}}",
        "sku": "{{context:product.sku}}",
        "price": {{context:product.price}},
        "category": "{{context:product.category}}",
        "description": "{{context:product.description}}"
      }
    output:
      createdId: "{{response.body.id}}"

endStage:
  output:
    generatedCount: "{{stage:generate-products.output.products.length?}}"
    createdCount: "{{stage:create-products.output.foreach_count}}"
    successCount: "{{stage:create-products.output.foreach_success_count}}"
    failedCount: "{{stage:create-products.output.foreach_failed_count}}"
    createdProducts: "{{stage:create-products.output.foreach_items}}"
    llmInputTokens: "{{stage:generate-products.output.inputTokens}}"
    llmOutputTokens: "{{stage:generate-products.output.outputTokens}}"
    llmTotalTokens: "{{stage:generate-products.output.totalTokens}}"
    llmFinishReason: "{{stage:generate-products.output.finishReason}}"
