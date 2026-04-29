# OpenAI LLM Stage Plugin

The built-in `openai` plugin calls an OpenAI-compatible Responses API endpoint. It owns `kind: LLM` and `kind: OpenAI`.

Use it when a workflow needs an LLM/SLM stage to transform data before another API request.

## Activation

```yaml
plugins:
  - openai
```

Declare the plugin and connection in `api.catalog`:

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
      config:
        model: "gpt-5.4-mini"
```

`connections[].config` can provide defaults such as `model`, `endpoint`, `organization`, `project`, and `reasoning.effort`. Stage config can override them.

## Stage

```yaml
stages:
  - name: "prepare-payload"
    kind: "LLM"
    expectedStatus: 200
    config:
      connectionRef: "openai-main"
      prompts:
        system:
          text: "You transform workflow data into API-ready JSON."
        input:
          file: "./prompts/create-customer.md"
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
          required: [name, country]
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

## Outputs

Default plugin outputs:

- `text`
- `inputTokens`
- `outputTokens`
- `totalTokens`
- `cachedInputTokens`
- `reasoningTokens`
- `finishReason`
- `durationMs`
- `requestId`
- `model`
- `provider`

Example:

```yaml
endStage:
  output:
    payload: "{{stage:prepare-payload.output.text}}"
    totalTokens: "{{stage:prepare-payload.output.totalTokens}}"
```

## Preflight

The OpenAI plugin uses `api.catalog` `connections`; it does not require OpenAPI/Swagger cache or endpoint validation.

Legacy `apiRef` plus `definitions` is still supported for compatibility, but new workflows should use `connectionRef`.

See [`samples/openai-llm/sample-openai-llm.workflow`](../samples/openai-llm/sample-openai-llm.workflow) for a runnable mocked example.
