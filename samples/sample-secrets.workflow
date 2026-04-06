version: "3.11"
id: "01J9SAMPLESECRETS000000001"
name: "sample-secrets"
description: "Demonstrates secret masking for inputs, variables, and outputs in the execution report."
output: true
references:
  apis:
    - name: "auth"
      definition: "auth"

input:
  - name: "username"
    type: "Text"
    required: true
    description: "The account username (visible in report)."
  - name: "apiKey"
    type: "Text"
    required: true
    secret: true
    description: "The API key used for authentication. Masked as ***** in the report."
  - name: "clientSecret"
    type: "Text"
    required: true
    secret: true
    description: "The OAuth client secret. Masked as ***** in the report."

initStage:
  variables:
    - name: "correlationId"
      type: "Ulid"
      # Not secret — useful for tracing, safe to show in report.
    - name: "nonce"
      type: "Guid"
      secret: true
      # Secret — generated one-time nonce, masked wherever its value appears in outputs.

stages:
  - name: "authenticate"
    kind: "Endpoint"
    apiRef: "auth"
    endpoint: "/api/auth/token"
    httpVerb: "POST"
    expectedStatus: 200
    headers:
      Content-Type: "application/json"
      X-Api-Key: "{{input.apiKey}}"
      X-Correlation-Id: "{{global:correlationId}}"
    body: |
      {
        "username": "{{input.username}}",
        "clientSecret": "{{input.clientSecret}}",
        "nonce": "{{global:nonce}}"
      }
    mock:
      status: 200
      payload: |
        {
          "accessToken": "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.mock",
          "refreshToken": "rt_mock_refresh_token_value",
          "expiresIn": 3600,
          "userId": "usr-00042"
        }
    output:
      authResponse: "{{response.body}}"
      accessToken: "{{stage:json(authenticate.output.authResponse).accessToken}}"
      refreshToken: "{{stage:json(authenticate.output.authResponse).refreshToken}}"
      userId: "{{stage:json(authenticate.output.authResponse).userId}}"
    secretOutputs:
      - accessToken    # The JWT is sensitive — masked in the stage record.
      - refreshToken   # Refresh token is sensitive — masked in the stage record.
    message: "Authentication result for {{input.username}}: HTTP {{response.status}}"

  - name: "get-profile"
    kind: "Endpoint"
    apiRef: "auth"
    endpoint: "/api/users/{{stage:authenticate.output.userId}}/profile"
    httpVerb: "GET"
    expectedStatus: 200
    headers:
      Authorization: "Bearer {{stage:authenticate.output.accessToken}}"
      X-Correlation-Id: "{{global:correlationId}}"
    mock:
      status: 200
      payload: |
        {
          "userId": "usr-00042",
          "username": "{{input.username}}",
          "email": "{{input.username}}@example.com",
          "roles": ["reader", "writer"]
        }
    output:
      profile: "{{response.body}}"
      email: "{{stage:json(get-profile.output.profile).email}}"
    message: "Profile loaded for {{input.username}}."

endStage:
  output:
    correlationId: "{{global:correlationId}}"
    userId: "{{stage:authenticate.output.userId}}"
    email: "{{stage:get-profile.output.email}}"
    accessToken: "{{stage:authenticate.output.accessToken}}"
    profile: "{{stage:get-profile.output.profile}}"
  secretOutputs:
    - accessToken   # Also masked in the final workflow output.
