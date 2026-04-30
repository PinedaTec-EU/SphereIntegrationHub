using System.Text.Json;

namespace SphereIntegrationHub.MCP.Tests.TestHelpers;

/// <summary>
/// Helper class to build test data for MCP server tests
/// </summary>
public static class TestDataBuilder
{
    /// <summary>
    /// Creates a sample Swagger document
    /// </summary>
    public static string CreateSampleSwagger(string apiName = "TestAPI")
    {
        return $$"""
        {
          "openapi": "3.0.0",
          "info": {
            "title": "{{apiName}}",
            "version": "1.0.0"
          },
          "servers": [
            { "url": "https://api.example.com" }
          ],
          "paths": {
            "/api/accounts": {
              "get": {
                "summary": "List accounts",
                "operationId": "listAccounts",
                "tags": ["Accounts"],
                "parameters": [
                  {
                    "name": "limit",
                    "in": "query",
                    "required": false,
                    "schema": { "type": "integer" },
                    "description": "Max results"
                  }
                ],
                "responses": {
                  "200": {
                    "description": "Success",
                    "content": {
                      "application/json": {
                        "schema": {
                          "type": "array",
                          "items": {
                            "type": "object",
                            "properties": {
                              "id": { "type": "string", "format": "uuid" },
                              "name": { "type": "string" },
                              "email": { "type": "string" }
                            }
                          }
                        }
                      }
                    }
                  }
                }
              },
              "post": {
                "summary": "Create account",
                "operationId": "createAccount",
                "tags": ["Accounts"],
                "requestBody": {
                  "required": true,
                  "content": {
                    "application/json": {
                      "schema": {
                        "type": "object",
                        "required": ["name", "email"],
                        "properties": {
                          "name": { "type": "string" },
                          "email": { "type": "string", "format": "email" },
                          "organizationId": { "type": "string", "format": "uuid" }
                        }
                      }
                    }
                  }
                },
                "responses": {
                  "201": {
                    "description": "Created",
                    "content": {
                      "application/json": {
                        "schema": {
                          "type": "object",
                          "properties": {
                            "id": { "type": "string", "format": "uuid" },
                            "name": { "type": "string" },
                            "email": { "type": "string" }
                          }
                        }
                      }
                    }
                  }
                }
              }
            },
            "/api/accounts/{id}": {
              "get": {
                "summary": "Get account by ID",
                "operationId": "getAccount",
                "tags": ["Accounts"],
                "parameters": [
                  {
                    "name": "id",
                    "in": "path",
                    "required": true,
                    "schema": { "type": "string", "format": "uuid" }
                  }
                ],
                "responses": {
                  "200": {
                    "description": "Success",
                    "content": {
                      "application/json": {
                        "schema": {
                          "type": "object",
                          "properties": {
                            "id": { "type": "string" },
                            "name": { "type": "string" },
                            "email": { "type": "string" }
                          }
                        }
                      }
                    }
                  }
                }
              },
              "put": {
                "summary": "Update account",
                "operationId": "updateAccount",
                "tags": ["Accounts"],
                "parameters": [
                  {
                    "name": "id",
                    "in": "path",
                    "required": true,
                    "schema": { "type": "string", "format": "uuid" }
                  }
                ],
                "requestBody": {
                  "required": true,
                  "content": {
                    "application/json": {
                      "schema": {
                        "type": "object",
                        "properties": {
                          "name": { "type": "string" },
                          "email": { "type": "string" }
                        }
                      }
                    }
                  }
                },
                "responses": {
                  "200": {
                    "description": "Updated",
                    "content": {
                      "application/json": {
                        "schema": {
                          "type": "object",
                          "properties": {
                            "id": { "type": "string" },
                            "name": { "type": "string" },
                            "email": { "type": "string" }
                          }
                        }
                      }
                    }
                  }
                }
              },
              "delete": {
                "summary": "Delete account",
                "operationId": "deleteAccount",
                "tags": ["Accounts"],
                "parameters": [
                  {
                    "name": "id",
                    "in": "path",
                    "required": true,
                    "schema": { "type": "string", "format": "uuid" }
                  }
                ],
                "responses": {
                  "204": { "description": "Deleted" }
                }
              }
            },
            "/oauth/token": {
              "post": {
                "summary": "Get OAuth token",
                "operationId": "getToken",
                "tags": ["Auth"],
                "requestBody": {
                  "required": true,
                  "content": {
                    "application/json": {
                      "schema": {
                        "type": "object",
                        "required": ["grant_type"],
                        "properties": {
                          "grant_type": { "type": "string" },
                          "client_id": { "type": "string" },
                          "client_secret": { "type": "string" }
                        }
                      }
                    }
                  }
                },
                "responses": {
                  "200": {
                    "description": "Token response",
                    "content": {
                      "application/json": {
                        "schema": {
                          "type": "object",
                          "properties": {
                            "access_token": { "type": "string" },
                            "token_type": { "type": "string" },
                            "expires_in": { "type": "integer" }
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
          },
          "components": {
            "securitySchemes": {
              "oauth2": {
                "type": "oauth2",
                "flows": {
                  "authorizationCode": {
                    "authorizationUrl": "https://auth.example.com/oauth/authorize",
                    "tokenUrl": "https://auth.example.com/oauth/token",
                    "scopes": {
                      "read": "Read access",
                      "write": "Write access"
                    }
                  }
                }
              }
            }
          }
        }
        """;
    }

    /// <summary>
    /// Creates a sample API catalog in YAML format (api.catalog)
    /// </summary>
    public static string CreateSampleApiCatalog()
    {
        return """
- version: "3.10"
  definitions:
  - name: AccountsAPI
    basePath: /api/accounts
    swaggerUrl: /swagger/accounts.json
    healthCheck: /health/accounts
    readiness:
      maxRetries: 3
      delayMs: 1000
      timeoutMs: 2000
      httpStatus:
      - 200
      - 204
    baseUrl:
      local: http://localhost
      pre: https://pre.api.example.com
  - name: UsersAPI
    basePath: /api/users
    swaggerUrl: /swagger/users.json
    baseUrl:
      local: http://localhost
      pre: https://pre.api.example.com
- version: "3.11"
  definitions:
  - name: AccountsAPI
    basePath: /api/accounts
    swaggerUrl: /swagger/accounts.json
    healthCheck: /health/accounts
    readiness:
      maxRetries: 3
      delayMs: 1000
      timeoutMs: 2000
      httpStatus:
      - 200
      - 204
    baseUrl:
      local: http://localhost
      pre: https://pre.api.example.com
  - name: UsersAPI
    basePath: /api/users
    swaggerUrl: /swagger/users.json
    baseUrl:
      local: http://localhost
      pre: https://pre.api.example.com
  - name: ProductsAPI
    basePath: /api/products
    swaggerUrl: /swagger/products.json
    baseUrl:
      local: http://localhost
      pre: https://pre.api.example.com
""";
    }

    /// <summary>
    /// Creates a sample workflow YAML
    /// </summary>
    public static string CreateSampleWorkflow(string name = "test-workflow", bool includeErrors = false)
    {
        if (includeErrors)
        {
            return @"
name: invalid-workflow
description: Workflow with errors
inputs:
  - userId

stages:
  - name: get-user
    plugin: HttpClientPlugin
    config:
      # Missing required fields
      method: GET

outputs:
  - source: {{ nonexistent.field }}
    target: result
";
        }

        return $@"
id: 01J7Z6J1KQZV8Y6J9G4E2ZB6QH
name: {name}
description: A test workflow
version: 1.0.0
references:
  apis:
    - name: AccountsAPI
      definition: AccountsAPI

input:
  - name: userId
    type: Text
    required: true
  - name: limit
    type: Number
    required: false

init-stage:
  variables:
    - name: baseUrl
      type: string
      value: https://api.example.com
    - name: apiVersion
      type: string
      value: v1

stages:
  - name: get-user
    kind: Endpoint
    apiRef: AccountsAPI
    endpoint: /api/accounts
    httpVerb: GET
    expectedStatus: 200

  - name: get-accounts
    kind: Endpoint
    apiRef: AccountsAPI
    endpoint: /api/accounts
    httpVerb: GET
    expectedStatus: 200
    query:
      limit: ""{{{{input.limit}}}}""
";
    }

    public static string CreateWorkflowWithObjectBody()
    {
        return """
id: 01J7Z6J1KQZV8Y6J9G4E2ZB6QH
name: invalid-body
description: Invalid workflow with body as an object
version: 1.0.0
references:
  apis:
    - name: AccountsAPI
      definition: AccountsAPI
stages:
  - name: create-account
    kind: Endpoint
    apiRef: AccountsAPI
    endpoint: /api/accounts
    httpVerb: POST
    expectedStatus: 201
    body:
      name: Test
      email: test@example.com
""";
    }

    public static string CreateWorkflowWithUndeclaredApiRef()
    {
        return """
id: 01J7Z6J1KQZV8Y6J9G4E2ZB6QH
name: invalid-api-ref
description: Invalid workflow with undeclared apiRef
version: 1.0.0
references:
  apis:
    - name: AccountsAPI
      definition: AccountsAPI
stages:
  - name: get-user
    kind: Endpoint
    apiRef: MissingAPI
    endpoint: /api/accounts
    httpVerb: GET
    expectedStatus: 200
""";
    }

    /// <summary>
    /// Creates a sample workflow with stages for testing
    /// </summary>
    public static Dictionary<string, object> CreateSampleWorkflowDict()
    {
        return new Dictionary<string, object>
        {
            ["name"] = "test-workflow",
            ["description"] = "A test workflow",
            ["inputs"] = new List<string> { "userId", "limit" },
            ["globals"] = new Dictionary<string, object>
            {
                ["baseUrl"] = "https://api.example.com"
            },
            ["stages"] = new List<Dictionary<string, object>>
            {
                new()
                {
                    ["name"] = "get-user",
                    ["plugin"] = "HttpClientPlugin",
                    ["config"] = new Dictionary<string, object>
                    {
                        ["url"] = "{{ global.baseUrl }}/users/{{ input.userId }}",
                        ["method"] = "GET"
                    },
                    ["saveResponseAs"] = "userData"
                },
                new()
                {
                    ["name"] = "get-accounts",
                    ["plugin"] = "HttpClientPlugin",
                    ["dependsOn"] = new List<string> { "get-user" },
                    ["config"] = new Dictionary<string, object>
                    {
                        ["url"] = "{{ global.baseUrl }}/accounts",
                        ["method"] = "GET"
                    },
                    ["saveResponseAs"] = "accountsData"
                }
            },
            ["outputs"] = new List<Dictionary<string, object>>
            {
                new()
                {
                    ["source"] = "{{ context.userData }}",
                    ["target"] = "result"
                }
            }
        };
    }

    /// <summary>
    /// Creates a sample stage definition
    /// </summary>
        public static Dictionary<string, object> CreateSampleStage(string name = "test-stage")
    {
        return new Dictionary<string, object>
        {
            ["name"] = name,
            ["type"] = "api",
            ["api"] = "AccountsAPI",
            ["endpoint"] = "/api/accounts",
            ["verb"] = "GET",
            ["saveResponseAs"] = "testData"
        };
    }

    /// <summary>
    /// Creates a sample invalid stage (missing required fields)
    /// </summary>
    public static Dictionary<string, object> CreateInvalidStage()
    {
        return new Dictionary<string, object>
        {
            ["name"] = "invalid-stage",
            ["type"] = "api",
            // Missing required 'api' and 'endpoint' fields - this makes it invalid
            ["saveResponseAs"] = "testData"
        };
    }

    /// <summary>
    /// Creates a sample endpoint info
    /// </summary>
    public static Dictionary<string, object> CreateSampleEndpointInfo()
    {
        return new Dictionary<string, object>
        {
            ["endpoint"] = "/api/accounts",
            ["httpVerb"] = "POST",
            ["summary"] = "Create account",
            ["tags"] = new List<string> { "Accounts" },
            ["queryParameters"] = new List<object>(),
            ["pathParameters"] = new List<object>(),
            ["bodySchema"] = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["name"] = new Dictionary<string, string> { ["type"] = "string" },
                    ["email"] = new Dictionary<string, string> { ["type"] = "string" }
                },
                ["required"] = new List<string> { "name", "email" }
            },
            ["responseSchema"] = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["id"] = new Dictionary<string, string> { ["type"] = "string" },
                    ["name"] = new Dictionary<string, string> { ["type"] = "string" },
                    ["email"] = new Dictionary<string, string> { ["type"] = "string" }
                }
            }
        };
    }
}
