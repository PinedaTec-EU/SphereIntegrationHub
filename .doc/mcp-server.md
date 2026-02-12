# MCP Server for SphereIntegrationHub

## Table of Contents
- [Vision and Goals](#vision-and-goals)
- [What is MCP?](#what-is-mcp)
- [Use Cases](#use-cases)
- [Architecture Overview](#architecture-overview)
- [MCP Tools by Capability Level](#mcp-tools-by-capability-level)
  - [Level 1: Assisted Mode (Basic)](#level-1-assisted-mode-basic)
  - [Level 2: Semi-Autonomous Mode](#level-2-semi-autonomous-mode)
  - [Level 3: Autonomous Mode](#level-3-autonomous-mode)
  - [Level 4: Optimizer Mode](#level-4-optimizer-mode)
- [Implementation Roadmap](#implementation-roadmap)
- [Technical Architecture](#technical-architecture)
- [Usage Examples](#usage-examples)
- [Getting Started](#getting-started)
- [Configuration Examples](#configuration-examples)
- [Verifying the Setup](#verifying-the-setup)

---

## Vision and Goals

The **SphereIntegrationHub MCP Server** exposes the intelligence and capabilities of SphereIntegrationHub as a set of tools that AI assistants (Claude, GitHub Copilot, Codex, etc.) can use to autonomously create, validate, and optimize complex API workflow orchestrations.

### Key Goals

1. **Accelerate Workflow Creation** - From hours to minutes by leveraging cached Swagger specifications
2. **Reduce Human Error** - Real-time validation prevents syntax and semantic errors
3. **Enable Autonomous Construction** - AI assistants can build complete systems from high-level descriptions
4. **Democratize API Orchestration** - Non-technical users can describe workflows in natural language
5. **Continuous Optimization** - AI can suggest improvements to existing workflows

### Why This Matters

Currently, creating a complex workflow in SphereIntegrationHub requires:
- Deep understanding of the workflow YAML schema
- Manual inspection of Swagger specs to find endpoints
- Manual mapping of data flows between stages
- Trial-and-error validation cycles

**With the MCP Server**, AI assistants can:
- ✅ Read all available APIs and endpoints from cached Swagger specs
- ✅ Understand schema requirements (required fields, types, auth)
- ✅ Infer dependencies between endpoints (e.g., "create account needs org ID first")
- ✅ Generate complete, valid workflows from natural language descriptions
- ✅ Suggest optimizations (parallelization, retry policies, error handling)

---

## What is MCP?

**MCP (Model Context Protocol)** is a standardized protocol for exposing tools and context to AI models. It allows applications to provide structured capabilities that AI assistants can invoke during conversations.

### Key Concepts

- **Tools**: Functions that AI can call with structured parameters (JSON schema)
- **Resources**: Documents or data that AI can read (Swagger specs, workflows)
- **Prompts**: Pre-defined prompts for common tasks
- **Transport**: stdio (standard input/output) for maximum compatibility

### Supported AI Clients

- Claude Desktop (Anthropic)
- VSCode with Claude Code extension
- GitHub Copilot (planned support)
- Cursor IDE
- Any client implementing MCP protocol

---

## Use Cases

### 1. Rapid Prototyping
**User:** "I need a workflow to create a customer account with payment method"

**AI with MCP:**
- Inspects API catalog → finds "customers" and "payments" APIs
- Reads Swagger specs → discovers `POST /api/customers` and `POST /api/payment-methods`
- Analyzes schemas → detects `customerId` dependency
- Generates complete workflow with auth, data flow, error handling

**Result:** Production-ready workflow in 30 seconds

---

### 2. Complex System Construction
**User:** "Build a hotel booking system: search availability, create reservation, process payment, send confirmation email"

**AI with MCP:**
- Analyzes 4 APIs from catalog (rooms, bookings, payments, notifications)
- Detects OAuth 2.0 authentication requirement
- Infers data dependencies: availability → room ID → booking → payment
- Generates workflow with 7 stages, proper context flow, retry policies
- Adds mock payloads for testing
- Creates test scenarios (happy path, no availability, payment failure)

**Result:** Enterprise-grade orchestration system in 2 minutes

---

### 3. Workflow Optimization
**User:** "This workflow is slow, can you optimize it?"

**AI with MCP:**
- Analyzes workflow structure
- Detects 2 GET requests that can run in parallel (no dependencies)
- Identifies redundant call (same endpoint called twice)
- Suggests adding circuit breaker to external payment API
- Proposes caching strategy for product catalog

**Result:** 40% faster execution, improved resilience

---

### 4. Documentation and Learning
**User:** "What APIs do we have for managing users?"

**AI with MCP:**
- Lists all APIs from catalog
- Filters Swagger specs for user-related endpoints
- Explains available operations with examples
- Shows existing workflows that use user APIs

**Result:** Instant API discovery and usage guidance

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                     AI Assistant (Claude, etc.)                  │
│                 "Create a workflow to onboard users"             │
└───────────────────────────────┬─────────────────────────────────┘
                                │
                                │ MCP Protocol (stdio)
                                ▼
┌─────────────────────────────────────────────────────────────────┐
│                   SphereIntegrationHub.MCP Server                │
│                                                                   │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │                    MCP Tool Handlers                       │  │
│  │  - list_api_catalog_versions                              │  │
│  │  - get_api_endpoints                                      │  │
│  │  - validate_workflow                                      │  │
│  │  - generate_workflow_skeleton                             │  │
│  │  - suggest_workflow_from_goal (L3)                        │  │
│  │  - analyze_endpoint_dependencies (L3)                     │  │
│  │  - ... (26 tools total across all levels)                 │  │
│  └───────────────────────────────────────────────────────────┘  │
│                                                                   │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │              SphereIntegrationHub Services                 │  │
│  │  (Reused from main CLI project)                           │  │
│  │                                                            │  │
│  │  - WorkflowValidator       - WorkflowLoader               │  │
│  │  - ApiEndpointValidator    - TemplateResolver             │  │
│  │  - ApiSwaggerCacheService  - WorkflowExecutor             │  │
│  └───────────────────────────────────────────────────────────┘  │
│                                                                   │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │                 New MCP-Specific Services                  │  │
│  │                                                            │  │
│  │  - SwaggerSemanticAnalyzer  (L2/L3)                       │  │
│  │    └─ Infers endpoint dependencies from schemas           │  │
│  │                                                            │  │
│  │  - WorkflowGraphBuilder  (L3)                             │  │
│  │    └─ Builds dependency graphs, topological sort          │  │
│  │                                                            │  │
│  │  - WorkflowSynthesizer  (L3)                              │  │
│  │    └─ Generates workflows from natural language goals     │  │
│  │                                                            │  │
│  │  - PatternDetector  (L2)                                  │  │
│  │    └─ Detects OAuth, CRUD, pagination patterns            │  │
│  └───────────────────────────────────────────────────────────┘  │
└───────────────────────────────┬─────────────────────────────────┘
                                │
                                │ Reads from
                                ▼
┌─────────────────────────────────────────────────────────────────┐
│                  SphereIntegrationHub Data Files                 │
│                                                                   │
│  - src/resources/api-catalog.json                                │
│  - src/resources/cache/{version}/{api}.json (Swagger specs)      │
│  - samples/*.workflow (Example workflows)                        │
│  - .doc/*.md (Documentation)                                     │
└─────────────────────────────────────────────────────────────────┘
```

---

## MCP Tools by Capability Level

### Level 1: Assisted Mode (Basic)

**Goal:** AI assists human developers by providing API exploration and validation tools.

**Human Role:** Makes architectural decisions, writes workflow structure
**AI Role:** Validates, suggests completions, finds endpoints

#### 1.1 Catalog Exploration (4 tools)

| Tool | Parameters | Returns | Purpose |
|------|------------|---------|---------|
| `list_api_catalog_versions` | - | `Array<string>` | Lists available catalog versions (3.10, 3.11, etc.) |
| `get_api_definitions` | `version: string` | `Array<ApiDefinition>` | Returns APIs in catalog with basePath and swagger URLs |
| `get_api_endpoints` | `version: string`<br>`apiName: string`<br>`httpVerb?: string` | `Array<EndpointInfo>` | Extracts endpoints from cached Swagger with schemas |
| `get_endpoint_schema` | `version: string`<br>`apiName: string`<br>`endpoint: string`<br>`httpVerb: string` | `EndpointSchema` | Returns detailed schema (query, headers, body, responses) |

**Example Usage:**
```
AI: Let me check what APIs are available...
→ list_api_catalog_versions()
→ get_api_definitions(version="3.11")
→ get_api_endpoints(version="3.11", apiName="accounts")
→ "I found 12 endpoints. POST /api/accounts requires organizationId"
```

#### 1.2 Workflow Validation (3 tools)

| Tool | Parameters | Returns | Purpose |
|------|------------|---------|---------|
| `validate_workflow` | `workflowYaml: string`<br>`environment?: string` | `ValidationResult` | Runs full WorkflowValidator, returns structured errors |
| `validate_stage` | `stageYaml: string`<br>`context: VariableContext`<br>`version: string` | `ValidationResult` | Validates individual stage with available variables |
| `plan_workflow_execution` | `workflowYaml: string`<br>`inputsYaml?: string`<br>`environment?: string` | `ExecutionPlan` | Executes --dry-run, returns step-by-step plan |

**ValidationResult Schema:**
```json
{
  "valid": false,
  "errors": [
    {
      "category": "TemplateResolution",
      "stage": "create-account",
      "field": "body",
      "message": "Token {{context.orgId}} is not defined",
      "suggestion": "Did you mean {{global:organizationId}}?",
      "location": "stages[2].body line 3"
    }
  ],
  "warnings": [
    {
      "category": "BestPractice",
      "message": "Endpoint POST /api/accounts has no retry policy"
    }
  ]
}
```

#### 1.3 Template Generation (3 tools)

| Tool | Parameters | Returns | Purpose |
|------|------------|---------|---------|
| `generate_endpoint_stage` | `version: string`<br>`apiName: string`<br>`endpoint: string`<br>`httpVerb: string`<br>`includeAuth: boolean`<br>`includeRetry: boolean` | `string` (YAML) | Generates complete endpoint stage from Swagger |
| `generate_workflow_skeleton` | `name: string`<br>`description: string`<br>`version: string`<br>`stages: Array<StageSpec>` | `string` (YAML) | Generates workflow with initStage, stages, endStage |
| `generate_mock_payload` | `version: string`<br>`apiName: string`<br>`endpoint: string`<br>`httpVerb: string`<br>`statusCode: number` | `string` (JSON) | Generates mock based on Swagger response schema |

**Example Output (generate_endpoint_stage):**
```yaml
- name: "create-account"
  kind: "Endpoint"
  apiRef: "accounts"
  endpoint: "/api/accounts"
  httpVerb: "POST"
  expectedStatus: 201
  headers:
    Authorization: "Bearer {{context.tokenId}}"
    Content-Type: "application/json"
  body: |
    {
      "name": "{{input.accountName}}",
      "organizationId": "{{global:orgId}}",
      "active": true
    }
  output:
    accountId: "{{response.body.id}}"
    accountName: "{{response.body.name}}"
  retry:
    maxRetries: 3
    delayMs: 250
    httpStatus: [500, 503]
  message: "Account created: {{response.body.id}}"
```

#### 1.4 Variable Analysis (3 tools)

| Tool | Parameters | Returns | Purpose |
|------|------------|---------|---------|
| `get_available_variables` | `workflowYaml: string`<br>`stageIndex: number` | `VariableScope` | Returns all accessible variables at stage position |
| `resolve_template_token` | `token: string`<br>`workflowYaml: string`<br>`stageIndex: number` | `TokenResolution` | Validates if token is resolvable at that point |
| `analyze_context_flow` | `workflowYaml: string` | `ContextFlowGraph` | Traces context propagation through stages |

**VariableScope Schema:**
```json
{
  "inputs": [
    {"name": "username", "type": "Text", "required": true}
  ],
  "globals": [
    {"name": "organizationId", "type": "Guid", "value": "<generated>"}
  ],
  "context": [
    {"name": "tokenId", "source": "stage:login.output.jwt"}
  ],
  "env": [
    {"name": "API_KEY", "value": "<hidden>"}
  ],
  "system": [
    {"token": "system:datetime.now", "description": "Current UTC timestamp"}
  ],
  "stageOutputs": [
    {
      "stage": "login",
      "outputs": [
        {"name": "jwt", "type": "string"},
        {"name": "userId", "type": "string"}
      ]
    }
  ]
}
```

#### 1.5 Workflow References (2 tools)

| Tool | Parameters | Returns | Purpose |
|------|------------|---------|---------|
| `list_available_workflows` | `basePath?: string` | `Array<WorkflowInfo>` | Lists .workflow files for referencing |
| `get_workflow_inputs_outputs` | `workflowPath: string` | `WorkflowInterface` | Extracts inputs required and outputs returned |

#### 1.6 Diagnostics (3 tools)

| Tool | Parameters | Returns | Purpose |
|------|------------|---------|---------|
| `explain_validation_error` | `errorMessage: string` | `ErrorExplanation` | Detailed explanation with fix suggestions |
| `get_plugin_capabilities` | `stageKind: string` | `PluginCapabilities` | Returns supported features (mocking, retry, etc.) |
| `suggest_resilience_config` | `endpoint: string`<br>`expectedLoad: "low"\|"medium"\|"high"` | `ResilienceConfig` | Suggests retry/circuit breaker settings |

**Total L1 Tools: 18**

---

### Level 2: Semi-Autonomous Mode

**Goal:** AI suggests complete workflows from goals but requires human approval.

**Human Role:** Provides high-level goals, reviews generated workflows
**AI Role:** Analyzes APIs, infers dependencies, generates workflow candidates

#### 2.1 Semantic Analysis (3 tools)

| Tool | Parameters | Returns | Purpose |
|------|------------|---------|---------|
| `analyze_endpoint_dependencies` | `version: string`<br>`targetEndpoint: string`<br>`httpVerb: string`<br>`apiName: string` | `EndpointDependencies` | Detects required prerequisite calls |
| `infer_data_flow` | `version: string`<br>`endpoints: Array<EndpointSpec>` | `DataFlowGraph` | Maps response fields to request fields |
| `suggest_workflow_from_goal` | `version: string`<br>`goal: string`<br>`includeAuth: boolean` | `WorkflowSuggestion` | Generates workflow from natural language |

**EndpointDependencies Schema:**
```json
{
  "endpoint": "POST /api/accounts",
  "requiredFields": [
    {
      "field": "organizationId",
      "type": "uuid",
      "location": "body",
      "possibleSources": [
        {
          "endpoint": "GET /api/organizations",
          "httpVerb": "GET",
          "apiName": "accounts",
          "responseField": "items[0].id",
          "confidence": 0.95,
          "reasoning": "Field name match + type match (uuid)"
        },
        {
          "endpoint": "POST /api/organizations",
          "httpVerb": "POST",
          "apiName": "accounts",
          "responseField": "id",
          "confidence": 0.90,
          "reasoning": "Field name match + type match + RESTful pattern"
        }
      ]
    },
    {
      "field": "Authorization",
      "type": "bearer-token",
      "location": "header",
      "possibleSources": [
        {
          "endpoint": "POST /oauth/token",
          "httpVerb": "POST",
          "apiName": "auth",
          "responseField": "access_token",
          "confidence": 1.0,
          "reasoning": "OAuth 2.0 pattern detected"
        }
      ]
    }
  ],
  "suggestedExecutionOrder": [
    {
      "step": 1,
      "endpoint": "POST /oauth/token",
      "reason": "Required for Authorization header"
    },
    {
      "step": 2,
      "endpoint": "GET /api/organizations",
      "reason": "Provides organizationId"
    },
    {
      "step": 3,
      "endpoint": "POST /api/accounts",
      "reason": "Target endpoint"
    }
  ]
}
```

**How analyze_endpoint_dependencies Works:**

1. **Schema Analysis**
   - Parses Swagger spec for target endpoint
   - Extracts required fields from body, query, headers
   - Identifies field types (uuid, string, integer, etc.)

2. **Source Detection**
   - Searches all endpoints in all APIs for matching response fields
   - Scoring based on:
     - Field name similarity (exact match = 1.0, partial = 0.5-0.9)
     - Type compatibility (uuid → uuid = 1.0, string → uuid = 0.3)
     - RESTful patterns (POST creates, GET retrieves)
     - Common authentication patterns (OAuth, JWT)

3. **Confidence Calculation**
   ```
   confidence = (name_match * 0.5) + (type_match * 0.3) + (pattern_match * 0.2)
   ```

4. **Topological Sort**
   - Builds dependency graph
   - Orders endpoints to satisfy all dependencies

#### 2.2 Pattern Detection (2 tools)

| Tool | Parameters | Returns | Purpose |
|------|------------|---------|---------|
| `detect_api_patterns` | `version: string`<br>`apiName: string` | `Array<ApiPattern>` | Detects OAuth, CRUD, pagination, etc. |
| `generate_crud_workflow` | `version: string`<br>`apiName: string`<br>`resource: string`<br>`operations: Array<string>` | `string` (YAML) | Generates full CRUD workflow |

**ApiPattern Schema:**
```json
{
  "patterns": [
    {
      "type": "OAuth2",
      "confidence": 1.0,
      "endpoints": {
        "token": "POST /oauth/token",
        "refresh": "POST /oauth/refresh"
      },
      "grantTypes": ["client_credentials", "password"],
      "tokenLocation": "response.access_token"
    },
    {
      "type": "CRUD",
      "confidence": 0.95,
      "resource": "accounts",
      "endpoints": {
        "list": "GET /api/accounts",
        "get": "GET /api/accounts/{id}",
        "create": "POST /api/accounts",
        "update": "PUT /api/accounts/{id}",
        "delete": "DELETE /api/accounts/{id}"
      },
      "idParameter": "id",
      "idType": "uuid"
    },
    {
      "type": "Pagination",
      "confidence": 1.0,
      "mechanism": "offset-limit",
      "queryParams": {
        "pageNumber": "page",
        "pageSize": "pageSize",
        "default": {"page": 1, "pageSize": 20}
      },
      "responseSchema": {
        "dataField": "items",
        "totalField": "total",
        "pageField": "page",
        "hasNextField": "hasMore"
      }
    },
    {
      "type": "Filtering",
      "confidence": 0.85,
      "queryParams": ["filter", "search", "status", "createdAfter"]
    }
  ]
}
```

**Total L2 Tools: 5 (cumulative: 23)**

---

### Level 3: Autonomous Mode

**Goal:** AI constructs complete, production-ready systems end-to-end from descriptions.

**Human Role:** Provides business requirements
**AI Role:** Full system design, implementation, testing

#### 3.1 Workflow Synthesis (1 tool)

This is the **"killer feature"** - full autonomous construction.

| Tool | Parameters | Returns | Purpose |
|------|------------|---------|---------|
| `synthesize_system_from_description` | `version: string`<br>`description: string`<br>`requirements: SystemRequirements` | `SystemDesign` | Generates complete system |

**SystemRequirements Schema:**
```json
{
  "description": "Hotel booking system with search, reservation, payment",
  "actors": ["guest", "admin"],
  "businessRules": [
    "Guest must be authenticated to book",
    "Payment required before confirmation",
    "Send email after successful booking"
  ],
  "nonfunctionalRequirements": {
    "availability": "high",
    "consistency": "eventual",
    "maxLatency": "5s"
  },
  "constraints": [
    "Use existing payment API",
    "No external dependencies for room search"
  ]
}
```

**SystemDesign Output:**
```json
{
  "workflows": [
    {
      "name": "guest-booking-flow",
      "path": "./workflows/guest-booking.workflow",
      "yaml": "...",
      "stages": 7,
      "description": "Main booking workflow for guests"
    },
    {
      "name": "auth-flow",
      "path": "./workflows/auth.workflow",
      "yaml": "...",
      "stages": 2,
      "description": "OAuth authentication"
    },
    {
      "name": "payment-processing",
      "path": "./workflows/payment.workflow",
      "yaml": "...",
      "stages": 4,
      "description": "Payment with retry and rollback"
    }
  ],
  "dependencies": [
    {
      "from": "guest-booking-flow",
      "to": "auth-flow",
      "reason": "Requires authentication token"
    },
    {
      "from": "guest-booking-flow",
      "to": "payment-processing",
      "reason": "Nested workflow for payment"
    }
  ],
  "testScenarios": [
    {
      "name": "happy-path",
      "description": "Successful booking end-to-end",
      "mockFile": "./mocks/happy-path.json"
    },
    {
      "name": "no-availability",
      "description": "No rooms available for dates",
      "expectedBehavior": "Return 404 at search stage"
    },
    {
      "name": "payment-failure",
      "description": "Payment declined",
      "expectedBehavior": "Rollback reservation, return error"
    }
  ],
  "apiUsage": {
    "auth": ["POST /oauth/token"],
    "rooms": ["GET /api/rooms", "GET /api/rooms/{id}"],
    "bookings": ["POST /api/bookings", "DELETE /api/bookings/{id}"],
    "payments": ["POST /api/payments"],
    "notifications": ["POST /api/emails"]
  },
  "estimatedComplexity": "medium",
  "estimatedExecutionTime": "3-5 seconds"
}
```

**How synthesize_system_from_description Works:**

1. **Intent Analysis**
   - Parse natural language description
   - Extract entities (customer, booking, payment)
   - Identify actions (create, search, process)
   - Detect constraints (authentication required, email notification)

2. **API Matching**
   - Search all cached Swagger specs for relevant endpoints
   - Scoring based on:
     - Tag matching ("booking", "reservation", "payment")
     - Path matching ("/bookings", "/payments")
     - Description matching (semantic similarity)
   - Group by functional domain

3. **Dependency Analysis**
   - For each matched endpoint, run `analyze_endpoint_dependencies`
   - Build complete dependency graph
   - Detect circular dependencies (flag as error)
   - Topological sort for execution order

4. **Workflow Composition**
   - Partition graph into workflows (max 10 stages per workflow)
   - Extract authentication as separate workflow (if OAuth detected)
   - Extract reusable components (payment processing, notifications)
   - Generate workflow references between them

5. **Data Flow Synthesis**
   - For each stage, generate input bindings from previous stage outputs
   - Example: `"customerId": "{{stage:create-customer.output.id}}"`
   - Add context propagation for authentication tokens

6. **Resilience Configuration**
   - Add retry policies to non-idempotent POSTs
   - Add circuit breakers to external APIs
   - Configure timeouts based on API documentation

7. **Test Generation**
   - Generate mock payloads for each endpoint
   - Create happy path scenario (all 200/201)
   - Create error scenarios (401, 404, 409, 500)
   - Add edge cases (missing fields, invalid types)

8. **Validation**
   - Run `validate_workflow` on all generated workflows
   - Run `plan_workflow_execution` to verify execution plan
   - Flag any unresolved dependencies or errors

**Total L3 Tools: 1 (cumulative: 24)**

---

### Level 4: Optimizer Mode

**Goal:** AI continuously improves existing workflows.

**Human Role:** Approves optimization suggestions
**AI Role:** Analyzes performance, suggests improvements

#### 4.1 Optimization (2 tools)

| Tool | Parameters | Returns | Purpose |
|------|------------|---------|---------|
| `suggest_optimizations` | `workflowYaml: string`<br>`constraints?: OptimizationConstraints` | `OptimizationReport` | Suggests performance improvements |
| `analyze_swagger_coverage` | `version: string` | `CoverageReport` | Shows unused endpoints, common patterns |

**OptimizationReport Schema:**
```json
{
  "workflow": "create-booking-flow",
  "currentMetrics": {
    "stages": 8,
    "estimatedDuration": "4.5s",
    "httpCalls": 7,
    "sequentialCalls": 7,
    "parallelizableCalls": 0
  },
  "optimizations": [
    {
      "type": "parallelization",
      "priority": "high",
      "stages": ["get-user-profile", "get-organization"],
      "reason": "No data dependencies between stages",
      "impact": {
        "durationReduction": "40%",
        "estimatedNewDuration": "2.7s"
      },
      "implementation": "Move stages to separate workflow, use Promise.all pattern"
    },
    {
      "type": "redundancy",
      "priority": "medium",
      "stage": "get-account-details-again",
      "reason": "Identical call to 'get-account-details' at stage 3",
      "impact": {
        "durationReduction": "12%",
        "networkCallsReduction": 1
      },
      "implementation": "Remove stage, reuse {{stage:get-account-details.output.dto}}"
    },
    {
      "type": "resilience",
      "priority": "high",
      "stage": "process-payment",
      "reason": "Critical POST endpoint without retry or circuit breaker",
      "impact": {
        "reliabilityImprovement": "Reduces failure rate by ~30%"
      },
      "implementation": {
        "retry": {
          "maxRetries": 3,
          "delayMs": 500,
          "httpStatus": [500, 503, 504]
        },
        "circuitBreaker": {
          "failureThreshold": 5,
          "breakMs": 60000
        }
      }
    },
    {
      "type": "caching",
      "priority": "low",
      "stage": "get-product-catalog",
      "reason": "GET endpoint called frequently, data changes infrequently",
      "impact": {
        "durationReduction": "90% (on cache hit)",
        "networkCallsReduction": "Up to 10 calls/minute"
      },
      "implementation": "Add local cache with 5-minute TTL"
    },
    {
      "type": "batching",
      "priority": "medium",
      "stages": ["create-item-1", "create-item-2", "create-item-3"],
      "reason": "API supports batch creation (POST /api/items/batch)",
      "impact": {
        "durationReduction": "66%",
        "networkCallsReduction": 2
      },
      "implementation": "Replace 3 stages with 1 batch call"
    }
  ],
  "projectedMetrics": {
    "stages": 6,
    "estimatedDuration": "1.5s",
    "httpCalls": 4,
    "improvementSummary": "67% faster, 3 fewer network calls"
  }
}
```

**Total L4 Tools: 2 (cumulative: 26)**

---

## Implementation Roadmap

### Phase 1: Foundation (2-3 weeks)
**Goal:** MCP server infrastructure + Level 1 tools

**Deliverables:**
- ✅ MCP server with stdio transport
- ✅ Tool registration and JSON schema definitions
- ✅ Integration with existing SphereIntegrationHub services
- ✅ All 18 Level 1 tools implemented
- ✅ Basic integration tests
- ✅ Documentation for tool usage

**Validation:**
- AI can explore API catalog
- AI can validate workflows
- AI can generate individual stages
- AI can check variable availability

**Branch:** `feature/mcp-server-integration-phase1`

---

### Phase 2: Semantic Analysis (3-4 weeks)
**Goal:** Level 2 semi-autonomous capabilities

**Deliverables:**
- ✅ SwaggerSemanticAnalyzer service
- ✅ PatternDetector service
- ✅ All 5 Level 2 tools implemented
- ✅ Confidence scoring for dependency detection
- ✅ Integration tests for pattern detection
- ✅ Documentation for semantic analysis

**New Services:**
```csharp
// Services/Semantic/SwaggerSemanticAnalyzer.cs
public class SwaggerSemanticAnalyzer
{
    Task<EndpointDependencies> AnalyzeDependenciesAsync(
        string version, string apiName, string endpoint, string verb);

    Task<List<FieldSource>> FindFieldSourcesAsync(
        string fieldName, string fieldType, string version);

    double CalculateConfidence(FieldMapping mapping);
}

// Services/Semantic/PatternDetector.cs
public class PatternDetector
{
    List<ApiPattern> DetectPatterns(OpenApiDocument swagger);

    OAuthPattern? DetectOAuth(OpenApiDocument swagger);
    CrudPattern? DetectCrud(OpenApiDocument swagger, string resource);
    PaginationPattern? DetectPagination(OpenApiDocument swagger);
}
```

**Validation:**
- AI can detect that "POST /accounts needs organizationId from GET /orgs"
- AI can identify OAuth 2.0 flow automatically
- AI can suggest complete workflow for "create account with payment"

**Branch:** `feature/mcp-server-integration-phase2`

---

### Phase 3: Autonomous Construction (4-6 weeks)
**Goal:** Level 3 full system synthesis

**Deliverables:**
- ✅ WorkflowGraphBuilder service (dependency graphs, topological sort)
- ✅ WorkflowSynthesizer service (natural language → workflow)
- ✅ Test scenario generator
- ✅ `synthesize_system_from_description` tool
- ✅ Advanced integration tests with complex scenarios
- ✅ Performance benchmarks (goal: <5s for typical system)

**New Services:**
```csharp
// Services/Synthesis/WorkflowGraphBuilder.cs
public class WorkflowGraphBuilder
{
    DependencyGraph BuildGraph(List<EndpointInfo> endpoints);
    List<string> TopologicalSort(DependencyGraph graph);
    List<Cycle> DetectCycles(DependencyGraph graph);
}

// Services/Synthesis/WorkflowSynthesizer.cs
public class WorkflowSynthesizer
{
    Task<SystemDesign> SynthesizeAsync(
        string description,
        SystemRequirements requirements);

    Dictionary<string, string> GenerateDataBindings(List<StageInfo> stages);

    List<TestScenario> GenerateTestScenarios(WorkflowDefinition workflow);
}

// Services/Synthesis/IntentAnalyzer.cs
public class IntentAnalyzer
{
    ParsedIntent ParseDescription(string naturalLanguage);
    List<string> ExtractEntities(string description);
    List<string> ExtractActions(string description);
}
```

**Validation:**
- AI can build complete hotel booking system from 2-sentence description
- Generated workflows pass validation
- Test scenarios cover happy path + error cases
- Execution plan is correct

**Branch:** `feature/mcp-server-integration-phase3`

---

### Phase 4: Optimization (2-3 weeks)
**Goal:** Level 4 continuous improvement

**Deliverables:**
- ✅ WorkflowOptimizer service
- ✅ All 2 Level 4 tools implemented
- ✅ Performance analysis engine
- ✅ Coverage reports
- ✅ Optimization validation (ensure suggested changes don't break workflows)

**New Services:**
```csharp
// Services/Optimization/WorkflowOptimizer.cs
public class WorkflowOptimizer
{
    OptimizationReport AnalyzeWorkflow(WorkflowDefinition workflow);

    List<Optimization> FindParallelizationOpportunities(WorkflowDefinition wf);
    List<Optimization> FindRedundantCalls(WorkflowDefinition wf);
    List<Optimization> SuggestResilienceImprovements(WorkflowDefinition wf);
    List<Optimization> FindCachingOpportunities(WorkflowDefinition wf);

    WorkflowDefinition ApplyOptimization(
        WorkflowDefinition workflow,
        Optimization optimization);
}

// Services/Optimization/SwaggerCoverageAnalyzer.cs
public class SwaggerCoverageAnalyzer
{
    CoverageReport AnalyzeCoverage(string version);
    List<string> FindUnusedEndpoints(string version);
    Dictionary<string, int> GetEndpointUsageStats();
}
```

**Validation:**
- AI correctly identifies parallelization opportunities
- Resilience suggestions are appropriate (no retry on idempotent POSTs)
- Coverage reports are accurate

**Branch:** `feature/mcp-server-integration-phase4`

---

### Phase 5: Polish & Release (2 weeks)
**Goal:** Production-ready MCP server

**Deliverables:**
- ✅ Complete documentation (this file + API reference)
- ✅ Installation guide for Claude Desktop, VSCode, etc.
- ✅ Example workflows showcasing each tool
- ✅ Performance optimizations (caching, async operations)
- ✅ Error handling and graceful degradation
- ✅ Logging and diagnostics
- ✅ Release binaries for macOS, Linux, Windows
- ✅ GitHub release with changelog

**Branch:** Merge to `main`

---

## Technical Architecture

### Project Structure

```
SphereIntegrationHub.MCP/
├── Program.cs                          # Entry point (stdio transport)
├── McpServer.cs                        # Main MCP server
│
├── Tools/                              # MCP tool implementations
│   ├── CatalogTools.cs                 # L1: list_versions, get_endpoints, etc.
│   ├── ValidationTools.cs              # L1: validate_workflow, plan_execution
│   ├── GenerationTools.cs              # L1: generate_stage, generate_skeleton
│   ├── AnalysisTools.cs                # L1: get_variables, resolve_token
│   ├── ReferenceTools.cs               # L1: list_workflows
│   ├── DiagnosticTools.cs              # L1: explain_error, suggest_resilience
│   ├── SemanticTools.cs                # L2: analyze_dependencies, infer_flow
│   ├── PatternTools.cs                 # L2: detect_patterns, generate_crud
│   ├── SynthesisTools.cs               # L3: synthesize_system
│   └── OptimizationTools.cs            # L4: suggest_optimizations, coverage
│
├── Services/
│   ├── Semantic/
│   │   ├── SwaggerSemanticAnalyzer.cs  # Endpoint dependency analysis
│   │   ├── FieldSourceFinder.cs        # Find sources for required fields
│   │   ├── ConfidenceScorer.cs         # Calculate match confidence
│   │   └── PatternDetector.cs          # OAuth, CRUD, pagination patterns
│   │
│   ├── Synthesis/
│   │   ├── WorkflowGraphBuilder.cs     # Dependency graphs, topological sort
│   │   ├── WorkflowSynthesizer.cs      # Natural language → workflow
│   │   ├── IntentAnalyzer.cs           # Parse natural language
│   │   ├── ApiMatcher.cs               # Match intents to APIs
│   │   └── TestScenarioGenerator.cs    # Generate test cases
│   │
│   ├── Optimization/
│   │   ├── WorkflowOptimizer.cs        # Suggest improvements
│   │   ├── PerformanceAnalyzer.cs      # Analyze execution plans
│   │   └── SwaggerCoverageAnalyzer.cs  # Endpoint usage stats
│   │
│   └── Integration/
│       └── SihServicesAdapter.cs       # Bridge to main CLI services
│
├── Models/                             # MCP-specific DTOs
│   ├── EndpointDependencies.cs
│   ├── DataFlowGraph.cs
│   ├── ApiPattern.cs
│   ├── SystemDesign.cs
│   ├── OptimizationReport.cs
│   └── ToolSchemas.cs                  # JSON schemas for all tools
│
└── Tests/
    ├── Tools/                          # Unit tests for each tool
    ├── Services/                       # Unit tests for services
    └── Integration/                    # End-to-end MCP scenarios
```

### Key Design Decisions

#### 1. Reuse Existing Services
The MCP server wraps existing SphereIntegrationHub services:
- `WorkflowValidator` → used by `validate_workflow` tool
- `WorkflowLoader` → used to parse workflows
- `ApiSwaggerCacheService` → reads cached Swagger specs
- `ApiEndpointValidator` → validates endpoints against specs

**Benefits:**
- No code duplication
- MCP server stays in sync with CLI behavior
- Bugs fixed in CLI automatically fix MCP

#### 2. Confidence Scoring
All semantic analysis returns confidence scores (0.0 to 1.0):
- **1.0** = Exact match (e.g., OAuth token endpoint)
- **0.9-0.95** = High confidence (e.g., field name + type match)
- **0.7-0.85** = Medium confidence (e.g., partial name match)
- **<0.7** = Low confidence (returned but flagged)

AI assistants can use confidence to:
- Auto-apply high-confidence suggestions
- Ask user to confirm medium-confidence suggestions
- Discard low-confidence suggestions

#### 3. Graceful Degradation
If advanced services fail (e.g., semantic analyzer), MCP falls back to basic tools:
- If `synthesize_system_from_description` fails → AI uses `generate_workflow_skeleton`
- If `analyze_endpoint_dependencies` fails → AI manually inspects schemas

#### 4. Performance Optimization
- **Lazy Loading**: Swagger specs loaded on demand, cached in memory
- **Async Operations**: All I/O operations are async
- **Parallel Analysis**: When analyzing multiple endpoints, use `Task.WhenAll`
- **Result Caching**: Cache dependency analysis results (TTL: 5 minutes)

#### 5. Security
- **Read-Only**: MCP server never modifies workflows or catalog (read-only access)
- **No Execution**: MCP cannot execute workflows (only validate/generate)
- **Sandboxing**: No file system access outside project directory

---

## Usage Examples

### Example 1: Assisted Workflow Creation (L1)

**User:** "I want to create a workflow that calls POST /api/accounts"

**AI with MCP:**

```
1. get_endpoint_schema(
     version="3.11",
     apiName="accounts",
     endpoint="/api/accounts",
     httpVerb="POST"
   )

   → Returns: {
       "requiredFields": {
         "body": ["name", "organizationId"],
         "headers": ["Authorization", "Content-Type"]
       },
       "bodySchema": {
         "name": "string",
         "organizationId": "uuid",
         "active": "boolean (optional)"
       },
       "responses": {
         "201": {"id": "uuid", "name": "string"},
         "400": {"error": "string"},
         "401": {"error": "string"}
       }
     }

2. generate_endpoint_stage(
     version="3.11",
     apiName="accounts",
     endpoint="/api/accounts",
     httpVerb="POST",
     includeAuth=true,
     includeRetry=true
   )

   → Returns complete YAML stage

3. get_available_variables(
     workflowYaml=<draft>,
     stageIndex=0
   )

   → AI sees: "You need to provide {{input.accountName}} and {{global:organizationId}}"

4. AI suggests: "Add organizationId as a global variable in initStage"

5. validate_workflow(workflowYaml=<complete draft>)

   → ✅ Valid

6. AI presents complete workflow to user for review
```

**Time:** ~30 seconds (vs. 10+ minutes manual)

---

### Example 2: Semi-Autonomous Construction (L2)

**User:** "Create a workflow to onboard a new customer with payment method"

**AI with MCP:**

```
1. get_api_definitions(version="3.11")
   → Finds "customers" and "payments" APIs

2. get_api_endpoints(version="3.11", apiName="customers")
   get_api_endpoints(version="3.11", apiName="payments")
   → Discovers: POST /api/customers, POST /api/payment-methods

3. analyze_endpoint_dependencies(
     version="3.11",
     apiName="payments",
     endpoint="/api/payment-methods",
     httpVerb="POST"
   )

   → Returns: {
       "requiredFields": [
         {
           "field": "customerId",
           "possibleSources": [
             {
               "endpoint": "POST /api/customers",
               "responseField": "id",
               "confidence": 0.95
             }
           ]
         },
         {
           "field": "Authorization",
           "possibleSources": [
             {
               "endpoint": "POST /oauth/token",
               "confidence": 1.0
             }
           ]
         }
       ],
       "suggestedExecutionOrder": [
         "POST /oauth/token",
         "POST /api/customers",
         "POST /api/payment-methods"
       ]
     }

4. detect_api_patterns(version="3.11", apiName="customers")
   → Detects OAuth 2.0 pattern

5. generate_workflow_skeleton(
     name="onboard-customer",
     version="3.11",
     stages=[
       {apiName: "auth", endpoint: "/oauth/token", verb: "POST"},
       {apiName: "customers", endpoint: "/api/customers", verb: "POST"},
       {apiName: "payments", endpoint: "/api/payment-methods", verb: "POST"}
     ]
   )

   → Generates complete workflow with:
      - OAuth stage
      - Create customer stage with data binding
      - Create payment method stage with customerId from previous stage

6. validate_workflow(workflowYaml=<generated>)
   → ✅ Valid

7. AI presents workflow: "I've created a 3-stage workflow. Review?"
```

**Time:** ~45 seconds (vs. 30+ minutes manual)

---

### Example 3: Fully Autonomous Construction (L3)

**User:** "I need a hotel booking system. Users search for available rooms, create a reservation, and pay. Send confirmation email after payment."

**AI with MCP:**

```
1. synthesize_system_from_description(
     version="3.11",
     description="hotel booking system with room search, reservation, payment, email",
     requirements={
       "businessRules": [
         "Authentication required",
         "Payment before confirmation",
         "Email after success"
       ],
       "nonfunctionalRequirements": {
         "availability": "high"
       }
     }
   )

   → MCP Server Process (internal):
      a. Intent Analysis
         - Entities: [user, room, reservation, payment, email]
         - Actions: [authenticate, search, create reservation, pay, notify]

      b. API Matching
         - Searches all Swagger specs
         - Finds: auth, rooms, bookings, payments, notifications APIs

      c. Dependency Analysis
         - Auth token → needed for all calls
         - Room ID → needed for booking
         - Booking ID → needed for payment
         - Payment confirmation → needed for email

      d. Workflow Composition
         - Main workflow: booking-flow (7 stages)
         - Referenced workflow: auth-flow (2 stages)
         - Referenced workflow: payment-with-rollback (4 stages)

      e. Data Flow Synthesis
         - Adds bindings: {{stage:auth.output.token}}
         - Adds bindings: {{stage:search.output.rooms[0].id}}
         - Adds bindings: {{stage:booking.output.id}}

      f. Resilience Configuration
         - Adds retry to payment stage
         - Adds circuit breaker to external payment API

      g. Test Generation
         - Creates 5 test scenarios with mocks

      h. Validation
         - Runs validate_workflow on all 3 workflows
         - Runs plan_workflow_execution to verify flow

   → Returns SystemDesign with 3 workflows, test scenarios, documentation

2. AI presents to user:
   "I've designed a 3-workflow system:

   1. auth-flow.workflow (authentication)
   2. payment-with-rollback.workflow (payment processing with error handling)
   3. booking-flow.workflow (main orchestration - 7 stages)

   The system handles:
   ✅ OAuth authentication
   ✅ Room availability search
   ✅ Reservation creation
   ✅ Payment processing with retry
   ✅ Email notification
   ✅ Automatic rollback on payment failure

   I've also generated 5 test scenarios. Would you like me to save these files?"
```

**Time:** ~10 seconds (vs. 2-3 hours manual)

---

### Example 4: Optimization (L4)

**User:** "This workflow is too slow, can you optimize it?"

**AI with MCP:**

```
1. suggest_optimizations(workflowYaml=<existing>)

   → Returns OptimizationReport:
   {
     "currentMetrics": {
       "stages": 8,
       "estimatedDuration": "4.5s"
     },
     "optimizations": [
       {
         "type": "parallelization",
         "priority": "high",
         "stages": ["get-user", "get-org"],
         "impact": {
           "durationReduction": "40%"
         }
       },
       {
         "type": "redundancy",
         "stage": "get-account-again",
         "impact": {
           "networkCallsReduction": 1
         }
       },
       {
         "type": "resilience",
         "stage": "payment",
         "reason": "Critical endpoint without retry"
       }
     ],
     "projectedMetrics": {
       "stages": 6,
       "estimatedDuration": "1.5s",
       "improvementSummary": "67% faster"
     }
   }

2. AI presents:
   "I found 3 optimization opportunities:

   🚀 HIGH PRIORITY - Parallelize 2 stages
   - get-user and get-org have no dependencies
   - Can run simultaneously
   - Saves 1.8s (40% faster)

   ♻️ MEDIUM PRIORITY - Remove redundant call
   - get-account-again duplicates get-account
   - Reuse existing output
   - Saves 1 network call

   🛡️ HIGH PRIORITY - Add resilience to payment
   - Payment stage has no retry
   - Should have circuit breaker
   - Improves reliability by ~30%

   Apply all optimizations? This will reduce execution from 4.5s to 1.5s"

3. User: "Yes, apply them"

4. AI modifies workflow YAML, applies optimizations

5. validate_workflow(workflowYaml=<optimized>)
   → ✅ Valid

6. AI: "✅ Optimizations applied. Workflow is now 67% faster."
```

**Time:** ~20 seconds (vs. hours of manual profiling/refactoring)

---

## Getting Started

This section walks you through the steps to build the MCP server and connect it to your AI assistant. No prior MCP knowledge is needed.

### Prerequisites

| Requirement | Why |
|-------------|-----|
| [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) | The MCP server is a .NET console application |
| An MCP-compatible AI agent | Claude Code, GitHub Copilot, ChatGPT Desktop, Cursor, etc. |

### Step 1 - Clone and build

```bash
git clone https://github.com/PinedaTec-EU/SphereIntegrationHub.git
cd SphereIntegrationHub

dotnet build src/SphereIntegrationHub.MCP
```

This compiles the MCP server. No Docker, no installers, no extra dependencies.

### Step 2 - Register the MCP server in your AI agent

Every MCP-compatible agent has a place where you declare: *"launch this command as an MCP server"*. The three pieces of information are always the same:

| What | Value |
|------|-------|
| **Command** | `dotnet` |
| **Arguments** | `run --project <path>/src/SphereIntegrationHub.MCP` |
| **Environment** | `SIH_PROJECT_ROOT=<path>` (repository root) |

See [Configuration Examples](#configuration-examples) below for the exact format for each agent.

### Step 3 - Restart or reload the agent

After saving the configuration, restart or reload the agent so it discovers the new server. On startup the agent will:

1. **Launch** the MCP server process (`dotnet run ...`)
2. **Connect** via stdio (JSON-RPC 2.0 over stdin/stdout)
3. **Discover** the 26 available tools by calling `tools/list`
4. **Incorporate** them as native tools alongside its built-in capabilities

### Step 4 - Start talking naturally

No special syntax. Just ask your AI assistant what you need:

> *"What APIs are available in version 3.11?"*
> *"Generate a workflow to create an account with payment method"*
> *"Validate this workflow and tell me what's wrong"*
> *"Build a complete hotel booking system with search, reservation, and payment"*

The agent decides which MCP tools to call and when. You don't invoke them directly.

### How it works under the hood

```
You ──────── "Create a workflow for user onboarding" ────────► AI Agent
                                                                  │
                                                          decides to call
                                                          MCP tools
                                                                  │
AI Agent ◄── list_api_catalog_versions() ──────────────► MCP Server
AI Agent ◄── get_api_endpoints("3.11", "accounts") ───► MCP Server
AI Agent ◄── analyze_endpoint_dependencies(...) ───────► MCP Server
AI Agent ◄── generate_workflow_skeleton(...) ───────────► MCP Server
AI Agent ◄── validate_workflow(...) ────────────────────► MCP Server
                                                                  │
You ◄──────── "Here's your validated workflow:" ─────────── AI Agent
```

---

## Configuration Examples

### Claude Code (VS Code extension / CLI)

Create a `.mcp.json` file at the **repository root**:

```json
{
  "mcpServers": {
    "sphere-integration-hub": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "src/SphereIntegrationHub.MCP"
      ],
      "env": {
        "SIH_PROJECT_ROOT": "."
      }
    }
  }
}
```

Or register it via the Claude Code CLI:

```bash
claude mcp add sphere-integration-hub \
  --command "dotnet" \
  --args "run,--project,src/SphereIntegrationHub.MCP" \
  --env "SIH_PROJECT_ROOT=."
```

**Usage:**
1. Open the SphereIntegrationHub project in VS Code
2. Open the Claude Code panel (or use the CLI)
3. Ask: *"Generate a workflow for user registration"*
4. Claude uses MCP tools automatically and shows the result in the editor

---

### GitHub Copilot (VS Code)

Create `.vscode/mcp.json` in the repository:

```json
{
  "servers": {
    "sphere-integration-hub": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "${workspaceFolder}/src/SphereIntegrationHub.MCP"
      ],
      "env": {
        "SIH_PROJECT_ROOT": "${workspaceFolder}"
      }
    }
  }
}
```

**Usage:**
1. Open the project in VS Code
2. Open **Copilot Chat** and switch to **Agent mode**
3. The MCP tools appear alongside Copilot's built-in tools
4. Ask naturally and Copilot will invoke the MCP tools when relevant

---

### ChatGPT Desktop

Open **Settings > MCP Servers > Add Server** and fill in:

| Field | Value |
|-------|-------|
| Name | `sphere-integration-hub` |
| Command | `dotnet` |
| Arguments | `run --project /absolute/path/to/SphereIntegrationHub/src/SphereIntegrationHub.MCP` |
| Environment | `SIH_PROJECT_ROOT=/absolute/path/to/SphereIntegrationHub` |

> ChatGPT Desktop requires **absolute paths** because it does not have a workspace concept.

**Usage:**
1. Open ChatGPT Desktop
2. The MCP tools are visible in the tools panel
3. Ask: *"What endpoints does the accounts API have?"*
4. ChatGPT calls the MCP server and returns the answer

---

### Claude Desktop

Edit the config file:

- **macOS:** `~/Library/Application Support/Claude/claude_desktop_config.json`
- **Windows:** `%APPDATA%\Claude\claude_desktop_config.json`

```json
{
  "mcpServers": {
    "sphere-integration-hub": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/absolute/path/to/SphereIntegrationHub/src/SphereIntegrationHub.MCP"
      ],
      "env": {
        "SIH_PROJECT_ROOT": "/absolute/path/to/SphereIntegrationHub"
      }
    }
  }
}
```

**Usage:**
1. Restart Claude Desktop
2. Start a conversation: *"Help me create a workflow to onboard users"*
3. Claude automatically uses MCP tools when needed
4. Tool calls are shown in the UI for transparency

---

### Cursor IDE

Open **Settings > MCP** and add a new server:

```json
{
  "sphere-integration-hub": {
    "command": "dotnet",
    "args": [
      "run",
      "--project",
      "/absolute/path/to/SphereIntegrationHub/src/SphereIntegrationHub.MCP"
    ],
    "env": {
      "SIH_PROJECT_ROOT": "/absolute/path/to/SphereIntegrationHub"
    }
  }
}
```

---

## Verifying the Setup

### Quick check from the agent

Ask your AI assistant:

> *"List the available API catalog versions"*

If the MCP server is connected correctly, the agent will call `list_api_catalog_versions` and return the available versions (e.g., `3.10`, `3.11`).

### Manual test from the terminal

```bash
echo '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}' | \
  SIH_PROJECT_ROOT=. dotnet run --project src/SphereIntegrationHub.MCP
```

You should see a JSON response with the server capabilities and version.

### Troubleshooting

| Problem | Cause | Solution |
|---------|-------|----------|
| Agent doesn't show MCP tools | Config file not found or wrong path | Double-check the config file location and command path |
| `SIH_PROJECT_ROOT` error | Environment variable not set or wrong | Ensure it points to the repository root (where `src/resources/` lives) |
| `dotnet` command not found | .NET SDK not installed or not in PATH | Install [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) and restart your terminal |
| Build errors | Missing dependencies | Run `dotnet restore src/SphereIntegrationHub.MCP` first |
| Server starts but no tools | Project root missing `src/resources/` | Verify the path contains `src/resources/api-catalog.json` |

---

## Conclusion

The **SphereIntegrationHub MCP Server** transforms workflow creation from a manual, error-prone process into an AI-assisted (or fully autonomous) experience.

### Impact Summary

| Task | Manual Time | With MCP (L1) | With MCP (L3) |
|------|-------------|---------------|---------------|
| Simple workflow (3 stages) | 15 minutes | 1 minute | 10 seconds |
| Complex workflow (10+ stages) | 1-2 hours | 10 minutes | 30 seconds |
| Multi-workflow system | 4-8 hours | 1 hour | 2 minutes |
| Workflow optimization | 2-4 hours | N/A | 20 seconds |

### Success Metrics (Phase 5)

- ✅ 90% of generated workflows are valid on first try
- ✅ L3 system synthesis completes in <10 seconds
- ✅ Confidence scores have <5% false positive rate
- ✅ AI assistants successfully use MCP in 95% of workflow tasks

### Next Steps

1. **Review this document** - Ensure alignment with project goals
2. **Start Phase 1** - Implement basic MCP server + L1 tools
3. **Iterate based on feedback** - Adjust tool designs as needed
4. **Expand progressively** - Add L2, L3, L4 as foundation solidifies

---

## Related Documentation

- [Workflow Schema](.doc/workflow-schema.md) - Complete YAML specification
- [Swagger Catalog](.doc/swagger-catalog.md) - API catalog structure
- [Variables and Context](.doc/variables.md) - Variable scopes and context flow
- [Plugins](.doc/plugins.md) - Plugin system architecture
- [CLI Help](.doc/cli.md) - Command-line usage

---

**Document Version:** 1.0
**Last Updated:** 2026-02-10
**Status:** 🚧 Planning Phase
**Current Branch:** `feature/mcp-server-integration`
