version: "3.11"
id: "01HVSANDBOXCARTFLOW000001"
name: "create-customer-cart-and-add-item"
description: |
  Creates a fictitious customer, initializes their shopping cart, retrieves a
  sample catalog item, and adds that item to the customer's cart.
output: true
references:
  apis:
    - name: shoppingCart
      definition: shopping-cart-sandbox

input:
  - name: customerName
    type: Text
    required: true
  - name: customerEmail
    type: Text
    required: true
  - name: sampleItemId
    type: Text
    required: true
  - name: quantity
    type: Number
    required: true

stages:
  - name: create-customer
    kind: Endpoint
    apiRef: shoppingCart
    endpoint: /users
    httpVerb: POST
    expectedStatuses: [201]
    headers:
      Content-Type: application/json
    body: |
      {
        "name": "{{input.customerName}}",
        "email": "{{input.customerEmail}}"
      }
    output:
      customerId: "{{response.body.id}}"
      customerName: "{{response.body.name}}"
      customerEmail: "{{response.body.email}}"

  - name: initialize-cart
    kind: Endpoint
    apiRef: shoppingCart
    endpoint: /users/{{stage:create-customer.output.customerId}}/cart
    httpVerb: POST
    expectedStatuses: [201]
    headers:
      Content-Type: application/json
    output:
      cartCustomerId: "{{response.body.user_id}}"

  - name: find-sample-item
    kind: Endpoint
    apiRef: shoppingCart
    endpoint: /items
    httpVerb: GET
    expectedStatuses: [200]
    query:
      id: "{{input.sampleItemId}}"
    output:
      itemId: "{{response.body.0.id}}"
      itemName: "{{response.body.0.name}}"
      itemPrice: "{{response.body.0.price}}"

  - name: add-item-to-cart
    kind: Endpoint
    apiRef: shoppingCart
    endpoint: /users/{{stage:create-customer.output.customerId}}/cart/items/{{stage:find-sample-item.output.itemId}}
    httpVerb: POST
    expectedStatuses: [200]
    headers:
      Content-Type: application/json
    body: |
      {
        "quantity": {{input.quantity}}
      }
    output:
      cartCustomerId: "{{response.body.user_id}}"
      firstCartItemId: "{{response.body.lines.0.item.id}}"
      firstCartItemName: "{{response.body.lines.0.item.name}}"
      firstCartItemQuantity: "{{response.body.lines.0.quantity}}"

endStage:
  output:
    customerId: "{{stage:create-customer.output.customerId}}"
    customerName: "{{stage:create-customer.output.customerName}}"
    customerEmail: "{{stage:create-customer.output.customerEmail}}"
    cartCustomerId: "{{stage:add-item-to-cart.output.cartCustomerId}}"
    itemId: "{{stage:add-item-to-cart.output.firstCartItemId}}"
    itemName: "{{stage:add-item-to-cart.output.firstCartItemName}}"
    itemPrice: "{{stage:find-sample-item.output.itemPrice}}"
    quantity: "{{stage:add-item-to-cart.output.firstCartItemQuantity}}"
