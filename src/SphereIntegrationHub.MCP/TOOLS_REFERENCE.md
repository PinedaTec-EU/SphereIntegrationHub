# MCP Tools Quick Reference

Complete reference for all 18 Level 1 MCP tools.

## Catalog Tools (4)

### 1. list_api_catalog_versions
Lists all available API catalog versions.

**Arguments:** None

**Returns:**
```json
{
  "versions": ["3.10", "3.11"],
  "count": 2
}
```

**Use cases:**
- Discover available API versions
- Check version availability before other operations

---

### 2. get_api_definitions
Gets all API definitions for a specific version.

**Arguments:**
- `version` (string, required) - API catalog version (e.g., "3.10")

**Returns:**
```json
{
  "version": "3.10",
  "apis": [
    {
      "name": "accounts",
      "basePath": "/accountsapi",
      "swaggerUrl": "/accountsapi/swagger/v1.0/swagger.json"
    }
  ],
  "count": 7
}
```

**Use cases:**
- Explore APIs available in a version
- Find API names for endpoint queries

---

### 3. get_api_endpoints
Lists all endpoints from a specific API.

**Arguments:**
- `version` (string, required) - API catalog version
- `apiName` (string, required) - Name of the API

**Returns:**
```json
{
  "version": "3.10",
  "apiName": "accounts",
  "endpoints": [
    {
      "endpoint": "/api/accounts",
      "httpVerb": "GET",
      "summary": "Gets all accounts",
      "tags": ["Accounts"],
      "queryParameterCount": 2,
      "pathParameterCount": 0,
      "hasBody": false
    }
  ],
  "count": 15
}
```

**Use cases:**
- Browse available endpoints in an API
- Find endpoints for specific operations
- Understand API structure

---

### 4. get_endpoint_schema
Gets detailed schema for a specific endpoint including parameters, body, and responses.

**Arguments:**
- `version` (string, required) - API catalog version
- `apiName` (string, required) - Name of the API
- `endpoint` (string, required) - Endpoint path (e.g., "/api/accounts")
- `httpVerb` (string, required) - HTTP verb (GET, POST, PUT, DELETE, PATCH)

**Returns:**
```json
{
  "apiName": "accounts",
  "endpoint": "/api/accounts/{id}",
  "httpVerb": "GET",
  "summary": "Gets account by ID",
  "description": "Returns detailed account information",
  "queryParameters": [],
  "headerParameters": [],
  "pathParameters": [
    {
      "name": "id",
      "type": "integer",
      "required": true,
      "description": "Account ID"
    }
  ],
  "bodySchema": null,
  "responses": {
    "200": {
      "statusCode": 200,
      "description": "Success",
      "fields": { ... }
    }
  }
}
```

**Use cases:**
- Understand endpoint requirements
- Generate workflow stages
- Create mock payloads
- Validate request/response schemas

---

## Validation Tools (3)

### 5. validate_workflow
Validates a complete workflow YAML file.

**Arguments:**
- `workflowPath` (string, required) - Path to workflow YAML (absolute or relative to workflows dir)

**Returns:**
```json
{
  "valid": true,
  "errors": [],
  "warnings": [
    {
      "category": "Performance",
      "message": "Consider adding resilience policy",
      "suggestion": "Add retry configuration for API stages"
    }
  ]
}
```

**Use cases:**
- Validate workflow before execution
- Check for structural errors
- Get improvement suggestions

---

### 6. validate_stage
Validates a single stage definition.

**Arguments:**
- `stageDefinition` (object, required) - Stage definition as JSON object

**Returns:**
```json
{
  "valid": false,
  "errors": [
    {
      "category": "Stage",
      "field": "api",
      "message": "API stage requires 'api' field",
      "suggestion": "Add 'api' field with API name from catalog"
    }
  ],
  "warnings": []
}
```

**Use cases:**
- Validate stage before adding to workflow
- Check stage configuration
- Get specific error details

---

### 7. plan_workflow_execution
Analyzes workflow execution plan and dependencies.

**Arguments:**
- `workflowPath` (string, required) - Path to workflow YAML

**Returns:**
```json
{
  "workflowName": "create-account-workflow",
  "totalStages": 5,
  "stages": [
    {
      "name": "validate_input",
      "type": "transform",
      "order": 0,
      "dependencies": []
    },
    {
      "name": "create_account",
      "type": "api",
      "order": 1,
      "dependencies": ["validate_input"]
    }
  ],
  "estimatedDuration": "3.2s"
}
```

**Use cases:**
- Understand execution order
- Identify dependencies
- Estimate execution time
- Optimize workflow structure

---

## Generation Tools (3)

### 8. generate_endpoint_stage
Generates a workflow stage from an API endpoint.

**Arguments:**
- `version` (string, required) - API catalog version
- `apiName` (string, required) - Name of the API
- `endpoint` (string, required) - Endpoint path
- `httpVerb` (string, required) - HTTP verb
- `stageName` (string, optional) - Custom stage name

**Returns:**
```json
{
  "stageName": "get_accounts_id",
  "yaml": "name: get_accounts_id\ntype: api\napi: accounts\nendpoint: /api/accounts/{id}\nverb: GET\npathParams:\n  id: '{{ input.accountId }}'\noutput:\n  save: true\n  context: get_accounts_idResult"
}
```

**Use cases:**
- Quick stage creation from endpoints
- Learn proper stage structure
- Bootstrap workflow development

---

### 9. generate_workflow_skeleton
Generates a complete workflow skeleton.

**Arguments:**
- `name` (string, required) - Workflow name
- `description` (string, required) - Workflow description
- `inputParameters` (array of strings, optional) - Input parameter names

**Returns:**
```json
{
  "name": "my-workflow",
  "yaml": "name: my-workflow\nversion: '1.0'\ndescription: My workflow description\ninput:\n  - name: userId\n    type: string\n    required: true\nstages:\n  - name: example_stage\n    type: api\n    api: your-api-name\n    endpoint: /api/endpoint\n    verb: GET\nend-stage:\n  output:\n    result: '{{ stages.example_stage.output }}'"
}
```

**Use cases:**
- Start new workflows quickly
- Get proper workflow structure
- Learn YAML format

---

### 10. generate_mock_payload
Generates a mock JSON payload for an endpoint.

**Arguments:**
- `version` (string, required) - API catalog version
- `apiName` (string, required) - Name of the API
- `endpoint` (string, required) - Endpoint path
- `httpVerb` (string, required) - HTTP verb (POST, PUT, PATCH)

**Returns:**
```json
{
  "endpoint": "/api/accounts",
  "httpVerb": "POST",
  "payload": "{\n  \"username\": \"example_string\",\n  \"email\": \"user@example.com\",\n  \"active\": true\n}"
}
```

**Use cases:**
- Test API endpoints
- Understand request structure
- Create test data
- Validate payload schemas

---

## Analysis Tools (3)

### 11. get_available_variables
Gets all available variables at a specific point in workflow.

**Arguments:**
- `workflowPath` (string, required) - Path to workflow YAML
- `atStage` (string, optional) - Stage name to analyze variables at that point

**Returns:**
```json
{
  "inputs": [
    {
      "name": "userId",
      "type": "string",
      "required": true
    }
  ],
  "globals": [],
  "context": [],
  "env": [
    {
      "name": "ENVIRONMENT",
      "value": "local"
    }
  ],
  "system": [
    {
      "token": "system.timestamp",
      "description": "Current timestamp"
    }
  ],
  "stageOutputs": [
    {
      "stage": "fetch_user",
      "outputs": [
        {
          "name": "status",
          "type": "integer"
        },
        {
          "name": "body",
          "type": "object"
        }
      ]
    }
  ]
}
```

**Use cases:**
- See what variables are available
- Debug template tokens
- Understand variable scope
- Write correct stage references

---

### 12. resolve_template_token
Resolves a template token to its source and type.

**Arguments:**
- `workflowPath` (string, required) - Path to workflow YAML
- `token` (string, required) - Template token (e.g., "input.userId" or "{{ input.userId }}")

**Returns:**
```json
{
  "token": "input.userId",
  "valid": true,
  "source": "Workflow input parameter",
  "type": "string",
  "required": true
}
```

**Use cases:**
- Validate template tokens
- Understand token sources
- Debug variable references
- Learn templating syntax

---

### 13. analyze_context_flow
Analyzes how context flows through workflow stages.

**Arguments:**
- `workflowPath` (string, required) - Path to workflow YAML

**Returns:**
```json
{
  "workflowName": "my-workflow",
  "stages": [
    {
      "stageName": "fetch_user",
      "contextReads": [],
      "contextWrites": ["userContext"]
    },
    {
      "stageName": "update_account",
      "contextReads": ["context.userContext"],
      "contextWrites": []
    }
  ]
}
```

**Use cases:**
- Understand context usage
- Find context dependencies
- Optimize context usage
- Debug context-related issues

---

## Reference Tools (2)

### 14. list_available_workflows
Lists all workflow files in the project.

**Arguments:** None

**Returns:**
```json
{
  "workflows": [
    {
      "path": "create-account.workflow",
      "absolutePath": "/path/to/workflows/create-account.workflow",
      "name": "create-account-workflow",
      "version": "1.0",
      "description": "Creates a new account",
      "stageCount": 5
    }
  ],
  "count": 10,
  "workflowsPath": "/path/to/workflows"
}
```

**Use cases:**
- Discover existing workflows
- Browse workflow library
- Find workflows for reference
- Check workflow metadata

---

### 15. get_workflow_inputs_outputs
Gets input parameters and output schema for a workflow.

**Arguments:**
- `workflowPath` (string, required) - Path to workflow file (`.workflow`, `.yaml`, `.yml`)

**Returns:**
```json
{
  "workflowName": "create-account-workflow",
  "inputs": [
    {
      "name": "username",
      "type": "string",
      "required": true,
      "description": "User's login name"
    }
  ],
  "inputCount": 3,
  "outputs": {
    "accountId": "{{ stages.create_account.output.body.id }}",
    "status": "success"
  },
  "outputCount": 2
}
```

**Use cases:**
- Understand workflow interface
- Compose workflows
- Document workflows
- Validate workflow calls

---

## Diagnostic Tools (3)

### 16. explain_validation_error
Explains validation errors with detailed suggestions.

**Arguments:**
- `errorCategory` (string, required) - Error category (e.g., "Stage", "Workflow")
- `errorMessage` (string, required) - The error message
- `context` (object, optional) - Additional context

**Returns:**
```json
{
  "category": "Stage",
  "message": "Stage name is required",
  "explanation": "Every stage in a workflow must have a unique 'name' field. The stage name is used to reference the stage's outputs...",
  "suggestions": [
    "Add a 'name' field with a descriptive, unique identifier",
    "Use lowercase with underscores (e.g., 'fetch_user_data')",
    "Avoid spaces and special characters in names"
  ],
  "examples": [
    {
      "description": "Correct stage with name",
      "code": "- name: fetch_user\n  type: api\n  api: accounts\n  endpoint: /api/users/{id}"
    }
  ],
  "relatedDocs": [
    "Workflow Stage Reference",
    "Best Practices"
  ]
}
```

**Use cases:**
- Understand validation errors
- Get fix suggestions
- Learn from examples
- Find relevant documentation

---

### 17. get_plugin_capabilities
Lists available plugin capabilities and stage types.

**Arguments:**
- `pluginType` (string, optional) - Specific plugin type to query

**Returns:**
```json
{
  "stageTypes": [
    {
      "type": "api",
      "description": "Makes HTTP requests to external APIs",
      "requiredFields": ["api", "endpoint"],
      "optionalFields": ["verb", "queryParams", "pathParams", "body"],
      "capabilities": ["HTTP requests", "Parameter templating", "Response capture"]
    }
  ],
  "features": [
    {
      "feature": "Template Tokens",
      "description": "Dynamic value substitution using {{ }} syntax",
      "examples": ["{{ input.userId }}", "{{ stages.fetch_user.output.body.name }}"]
    }
  ]
}
```

**Use cases:**
- Learn available stage types
- Understand stage capabilities
- Discover features
- Reference stage configuration

---

### 18. suggest_resilience_config
Suggests resilience configuration based on operation type.

**Arguments:**
- `stageType` (string, required) - Stage type (e.g., "api", "transform")
- `operation` (string, optional) - Operation description or endpoint
- `critical` (boolean, optional) - Whether this is a critical operation

**Returns:**
```json
{
  "stageType": "api",
  "operation": "GET /api/accounts",
  "critical": true,
  "recommendations": {
    "retry": {
      "enabled": true,
      "maxRetries": 5,
      "backoffType": "exponential",
      "initialDelay": "1s",
      "maxDelay": "30s",
      "reason": "Read operations are safe to retry"
    },
    "timeout": {
      "enabled": true,
      "timeout": "60s",
      "reason": "Prevents hanging on slow or unresponsive services"
    },
    "circuitBreaker": {
      "enabled": true,
      "failureThreshold": 5,
      "durationOfBreak": "30s",
      "reason": "Protects critical services from cascading failures"
    }
  },
  "example": {
    "yaml": "resilience:\n  - name: standard_api_policy\n    retry:\n      maxRetries: 5\n..."
  }
}
```

**Use cases:**
- Configure resilience policies
- Understand retry strategies
- Improve reliability
- Handle failures gracefully

---

## Common Patterns

### Pattern 1: Explore and Generate
```
1. list_api_catalog_versions → Get available versions
2. get_api_definitions → Find APIs in a version
3. get_api_endpoints → Browse endpoints
4. generate_endpoint_stage → Create stage from endpoint
```

### Pattern 2: Validate and Fix
```
1. validate_workflow → Identify errors
2. explain_validation_error → Understand the error
3. get_plugin_capabilities → Learn correct format
4. validate_workflow → Verify fix
```

### Pattern 3: Understand Variables
```
1. get_available_variables → See all variables
2. resolve_template_token → Verify specific token
3. analyze_context_flow → Understand dependencies
```

### Pattern 4: Create New Workflow
```
1. generate_workflow_skeleton → Create base structure
2. get_api_endpoints → Find needed endpoints
3. generate_endpoint_stage → Add stages
4. validate_workflow → Check result
```

## Tips

- Use relative paths for `workflowPath` (resolved from workflows directory)
- Template tokens can include or omit `{{ }}` braces
- All tools return JSON that can be parsed and used programmatically
- Error messages include suggestions for common mistakes
- Mock payloads are schema-aware and type-appropriate
