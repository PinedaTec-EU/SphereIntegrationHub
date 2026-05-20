version: "3.11"
id: "01HVDEMOACCOUNTSCART000001"
name: "create-account-and-add-cart-item"
description: |
  Creates a customer account and synchronizes the returned account id into the
  cart item creation step.
output: true
references:
  apis:
    - name: commerce
      definition: commerce-demo

input:
  - name: accountName
    type: Text
    required: true
  - name: email
    type: Text
    required: true
  - name: sku
    type: Text
    required: true
  - name: quantity
    type: Number
    required: true

stages:
  - name: create-account
    kind: Endpoint
    apiRef: commerce
    endpoint: /accounts
    httpVerb: POST
    expectedStatuses: [201]
    headers:
      Content-Type: application/json
    body: |
      {
        "name": "{{input.accountName}}",
        "email": "{{input.email}}"
      }
    output:
      accountId: "{{response.body.accountId}}"
      cartId: "{{response.body.cartId}}"

  - name: add-cart-item
    kind: Endpoint
    apiRef: commerce
    endpoint: /carts/{{stage:create-account.output.cartId}}/items
    httpVerb: POST
    expectedStatuses: [201]
    headers:
      Content-Type: application/json
    body: |
      {
        "accountId": "{{stage:create-account.output.accountId}}",
        "sku": "{{input.sku}}",
        "quantity": {{input.quantity}}
      }
    output:
      itemId: "{{response.body.itemId}}"
      synchronizedAccountId: "{{response.body.accountId}}"

endStage:
  output:
    accountId: "{{stage:create-account.output.accountId}}"
    cartId: "{{stage:create-account.output.cartId}}"
    itemId: "{{stage:add-cart-item.output.itemId}}"
    synchronized: "{{stage:create-account.output.accountId == stage:add-cart-item.output.synchronizedAccountId}}"
