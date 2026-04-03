version: "3.11"
id: "01JMCPROBE00000000000001"
name: "mcp-probe"
description: "Validates an MCP server via JSON-RPC/HTTP: initialize → tools/list → tools/call. Mocks mirror the real sphere-integration-hub MCP responses."
output: true
references:
  apis:
    - name: "mcp"
      definition: "mcp"
input:
  - name: "toolName"
    type: "Text"
    required: false

stages:
  - name: "initialize"
    kind: "Endpoint"
    apiRef: "mcp"
    endpoint: "/"
    httpVerb: "POST"
    expectedStatuses: [200]
    headers:
      Content-Type: "application/json"
    body: |
      {
        "jsonrpc": "2.0",
        "id": 1,
        "method": "initialize",
        "params": {
          "protocolVersion": "2024-11-05",
          "capabilities": {},
          "clientInfo": { "name": "sphere-mcp-probe", "version": "1.0" }
        }
      }
    mock:
      payload: |
        {
          "jsonrpc": "2.0",
          "id": 1,
          "result": {
            "protocolVersion": "2024-11-05",
            "capabilities": { "tools": {} },
            "serverInfo": { "name": "SphereIntegrationHub.MCP", "version": "0.5.5.115" }
          }
        }
    output:
      serverName: "{{response.body.result.serverInfo.name}}"
      serverVersion: "{{response.body.result.serverInfo.version}}"
      protocol: "{{response.body.result.protocolVersion}}"
    message: "MCP ready: {{response.body.result.serverInfo.name}} v{{response.body.result.serverInfo.version}}"

  - name: "list-tools"
    kind: "Endpoint"
    apiRef: "mcp"
    endpoint: "/"
    httpVerb: "POST"
    expectedStatuses: [200]
    headers:
      Content-Type: "application/json"
    body: |
      {
        "jsonrpc": "2.0",
        "id": 2,
        "method": "tools/list",
        "params": {}
      }
    mock:
      payload: |
        {
          "jsonrpc": "2.0",
          "id": 2,
          "result": {
            "tools": [
              { "name": "get_plugin_capabilities", "description": "Gets information about available plugin capabilities and stage types" },
              { "name": "validate_workflow", "description": "Validates a complete workflow YAML file for structure, syntax, and semantic correctness" },
              { "name": "validate_stage", "description": "Validates a single stage definition for correctness" },
              { "name": "generate_workflow_skeleton", "description": "Generates a minimal workflow YAML skeleton from a description" },
              { "name": "suggest_workflow_from_goal", "description": "Suggests a workflow structure based on a high-level goal description" }
            ]
          }
        }
    output:
      tools: "{{response.body.result.tools}}"
    message: "Discovered {{response.body.result.tools.length}} tools on server."

  - name: "call-tool"
    kind: "Endpoint"
    apiRef: "mcp"
    endpoint: "/"
    httpVerb: "POST"
    expectedStatuses: [200]
    headers:
      Content-Type: "application/json"
    body: |
      {
        "jsonrpc": "2.0",
        "id": 3,
        "method": "tools/call",
        "params": {
          "name": "{{input.toolName}}",
          "arguments": {}
        }
      }
    mock:
      payload: |
        {
          "jsonrpc": "2.0",
          "id": 3,
          "result": {
            "content": [
              {
                "type": "text",
                "text": "{\"stageTypes\":[{\"type\":\"endpoint\",\"description\":\"Calls an HTTP endpoint using the runtime workflow schema\"},{\"type\":\"workflow\",\"description\":\"Executes another workflow using workflowRef\"}]}"
              }
            ]
          }
        }
    output:
      toolResult: "{{response.body.result.content.0.text}}"
    message: "Tool '{{input.toolName}}' invoked successfully."

endStage:
  output:
    server: "{{stage:initialize.output.serverName}}"
    version: "{{stage:initialize.output.serverVersion}}"
    protocol: "{{stage:initialize.output.protocol}}"
    tools: "{{stage:list-tools.output.tools}}"
    callResult: "{{stage:call-tool.output.toolResult}}"
