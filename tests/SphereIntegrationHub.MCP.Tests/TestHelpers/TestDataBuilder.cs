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
    /// Creates a sample API catalog JSON
    /// </summary>
    public static string CreateSampleApiCatalog()
    {
        // ApiCatalogReader expects a JSON array, not an object with "versions" property
        var catalog = new[]
        {
            new
            {
                Version = "3.10",
                BaseUrl = new Dictionary<string, string>
                {
                    ["local"] = "http://localhost",
                    ["pre"] = "https://pre.api.example.com"
                },
                Definitions = new[]
                {
                    new
                    {
                        Name = "AccountsAPI",
                        BasePath = "/api/accounts",
                        SwaggerUrl = "https://api.example.com/swagger/accounts.json"
                    },
                    new
                    {
                        Name = "UsersAPI",
                        BasePath = "/api/users",
                        SwaggerUrl = "https://api.example.com/swagger/users.json"
                    }
                }
            },
            new
            {
                Version = "3.11",
                BaseUrl = new Dictionary<string, string>
                {
                    ["local"] = "http://localhost",
                    ["pre"] = "https://pre.api.example.com"
                },
                Definitions = new[]
                {
                    new
                    {
                        Name = "AccountsAPI",
                        BasePath = "/api/accounts",
                        SwaggerUrl = "https://api.example.com/swagger/accounts.json"
                    },
                    new
                    {
                        Name = "UsersAPI",
                        BasePath = "/api/users",
                        SwaggerUrl = "https://api.example.com/swagger/users.json"
                    },
                    new
                    {
                        Name = "ProductsAPI",
                        BasePath = "/api/products",
                        SwaggerUrl = "https://api.example.com/swagger/products.json"
                    }
                }
            }
        };

        return JsonSerializer.Serialize(catalog, new JsonSerializerOptions { WriteIndented = true });
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
name: {name}
description: A test workflow
version: 1.0.0

input:
  - name: userId
    type: string
    required: true
  - name: limit
    type: integer
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
    plugin: HttpClientPlugin
    config:
      url: ""{{{{ global.baseUrl }}}}/users/{{{{ input.userId }}}}""
      method: GET
      headers:
        Authorization: Bearer {{{{ env.API_TOKEN }}}}
    saveResponseAs: userData

  - name: get-accounts
    plugin: HttpClientPlugin
    dependsOn:
      - get-user
    config:
      url: ""{{{{ global.baseUrl }}}}/accounts""
      method: GET
      queryParameters:
        userId: ""{{{{ context.userData.id }}}}""
        limit: ""{{{{ input.limit }}}}""
    saveResponseAs: accountsData

  - name: process-data
    plugin: DataTransformPlugin
    dependsOn:
      - get-accounts
    config:
      transformation: |
        {{
          user: context.userData,
          accounts: context.accountsData,
          totalAccounts: context.accountsData.length
        }}
    saveResponseAs: processedData

outputs:
  - source: {{{{ context.processedData }}}}
    target: result
";
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
