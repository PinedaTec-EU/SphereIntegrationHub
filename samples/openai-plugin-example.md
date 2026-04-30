# OpenAI plugin example

This sample shows the runtime shape for an LLM/SLM stage backed by the built-in `openai` plugin. A complete runnable mocked workflow is available under `samples/openai-llm/`.

1. `workflows.config`

```yaml
plugins:
  - http
  - openai
```

2. `api.catalog`

```yaml
- version: 3.11
  plugins:
    - id: openai
      contractVersion: "1.0"
      runtimeVersion: "1.0"
  definitions: []
  connections:
    - name: openai-main
      type: llm
      provider: openai
      baseUrl:
        local: https://api.openai.com/v1
      apiKeySecret: "{{input.openaiApiKey}}"
```

3. workflow stage

```yaml
input:
  - name: openaiApiKey
    type: Text
    required: true
    secret: true

stages:
  - name: "prepare-api-request"
    kind: "LLM"
    config:
      connectionRef: "openai-main"
      model: "gpt-5.4-mini"
      prompts:
        system:
          text: "You transform workflow data into API-ready JSON."
        input:
          file: "./prompts/create-customer.md"
        output:
          text: "Return only the request payload."
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
```

Default stage outputs include `text`, `inputTokens`, `outputTokens`, `totalTokens`, `cachedInputTokens`, `reasoningTokens`, `finishReason`, `durationMs`, `requestId`, `model`, and `provider`.
