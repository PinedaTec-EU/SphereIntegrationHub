version: "3.11"
id: "01JFAKEUSAGESEED000000001"
name: "fake-usage-seed"
description: "Lists matching customers and posts random usage events for each returned id."
output: true
references:
  apis:
    - name: "customers"
      definition: "customers"
    - name: "usage"
      definition: "usage"

input:
  - name: "jwt"
    type: "Text"
    required: true
  - name: "segment"
    type: "Text"
    required: true

stages:
  - name: "list-customers"
    kind: "Endpoint"
    apiRef: "customers"
    endpoint: "/api/customers/search?segment={{input.segment}}"
    httpVerb: "GET"
    expectedStatuses: [200]
    headers:
      Authorization: "Bearer {{input.jwt}}"
    output:
      customers: "{{response.body.items}}"
      count: "{{response.body.items.length?}}"

  - name: "seed-usage"
    kind: "Endpoint"
    apiRef: "usage"
    endpoint: "/api/usage"
    httpVerb: "POST"
    expectedStatuses: [200, 201]
    forEach: "{{stage:list-customers.output.customers}}"
    itemName: "customer"
    indexName: "customerIndex"
    headers:
      Content-Type: "application/json"
      Authorization: "Bearer {{input.jwt}}"
    body: |
      {
        "customerId": "{{context:customer.id}}",
        "serviceCode": "{{rand:text(10, 'alnum')}}",
        "units": {{rand:number(1, 25)}},
        "usedAt": "{{rand:datetime(system:datetime.utcnow - P30D, system:datetime.utcnow)}}",
        "reference": "{{rand:guid()}}"
      }
    output:
      created: "{{response.body}}"

endStage:
  output:
    matchedCustomers: "{{stage:list-customers.output.count}}"
    seededCount: "{{stage:seed-usage.output.foreach_count}}"
    seededItems: "{{stage:seed-usage.output.foreach_items}}"
