# LLM-Powered Test Data Generation: Stop Writing Scripts, Start Generating Reality

**Canvas para artículo técnico - C# Corner**

---

## 📋 ARTICLE OUTLINE

### 1. The Pain (Opening Hook)
### 2. What You're Doing Now (Current Approaches)
### 3. The LLM Solution
### 4. Implementation Deep Dive
### 5. Real-World Use Cases
### 6. When to Use (and Not Use)
### 7. Getting Started

---

## 🎯 TARGET AUDIENCE
- QA Engineers (test data generation)
- Backend Developers (seeding environments)
- DevOps Engineers (CI/CD test automation)
- Platform Teams (realistic staging data)

---

## 💡 KEY MESSAGES
1. Random test data is technically valid but useless for real testing
2. LLM can generate contextually realistic data
3. No external scripts, inline in workflows
4. Combine with forEach for bulk generation
5. Better test coverage with realistic scenarios

---

---

# 1. THE PAIN (Opening Hook)

## Scenario: E-Commerce Platform Testing

**Monday morning standup**:

**QA Engineer**: "I can't test the recommendation engine. All test users have garbage bios."

**Backend Dev**: "What do you mean? We have 1,000 test users in staging."

**QA**: "Yeah, look at this:"

```json
{
  "userId": "a3f2b5c8-e4d1-4f9a-b2c7-8e5f3a1d9c6b",
  "name": "Kdjfhaksld Mxnvbqwo",
  "email": "hgkdjfla@test.com",
  "bio": "Dfkjghkdfhgkljsdhfgkljshdfglkjhsdflgkjhsdfg",
  "interests": ["Qwerty", "Asdfgh", "Zxcvbn"]
}
```

**QA**: "The recommendation algorithm needs real interests like 'hiking', 'photography', 'cooking'. Not... 'Qwerty'."

**Backend Dev**: "Those are generated with `rand:text()`. It's random."

**QA**: "Exactly. Can you make them... not random?"

**Backend Dev**: "I'd need to write a Python script with Faker library, maintain interest taxonomies, generate CSVs, import them..."

**PM**: "How long?"

**Backend Dev**: "2 days to build, ongoing maintenance every time we add features."

**PM**: "We ship in 3 days."

**The problem**: Random data is **technically valid** but **functionally useless** for testing real features.

---

## What Features Can't You Test with Random Data?

Random test data breaks **5 critical areas**:

### 1. Recommendation Engines ❌
**Problem**: Algorithm needs semantic understanding
```
User interests: ["Asdfgh", "Qwerty"]
Recommended products: ???
```
Algorithm can't match "Asdfgh" to "Hiking boots" or "Camera lens"

### 2. Search & Filtering ❌
**Problem**: No real keywords to match
```
Search query: "Find users interested in photography"
Results: 0 matches
```
No user has "photography" — only "Xyzabc", "Dfkjgh", "Qwerty"

### 3. Content Personalization ❌
**Problem**: Personalization looks broken
```
User bio: "Kdjfhaksld mxnvbqwo dfkjghk"
Email: "Hi Kdjfhaksld, based on your interest in mxnvbqwo..."
```
Marketing team: "This looks like a bug, not a feature"

### 4. UI/UX Validation ❌
**Problem**: Can't validate real layouts
```
Designer: "How does the profile card look with real bios?"
QA: "All random strings. Can't tell if text wraps correctly."
```
Need realistic length/content to validate design

### 5. Analytics & Reporting ❌
**Problem**: Meaningless insights
```
Top user interests dashboard:
1. Dfkjgh (245 users)
2. Qwerty (198 users)  
3. Asdfgh (187 users)
```
Product team: "What does this even mean?"

---

### The Impact

**Features you CAN'T test**:
- ❌ Recommendation algorithms
- ❌ Search functionality
- ❌ Personalization engines
- ❌ Content moderation
- ❌ A/B test segmentation
- ❌ Analytics dashboards

**Test coverage gap**: every feature that depends on semantic data becomes harder to trust.

---

### 📊 **Quick Summary**

**The core problem**: Random test data = technically valid, functionally useless

**What this means**:
- ✅ Schema validation passes
- ❌ Real features can't be tested
- ❌ Semantic test coverage gap
- ❌ Bugs slip to production

**Next**: Let's look at what teams do today to solve this...

---

## The Cost

**Time wasted**:
- QA: "I can't test this feature with random data"
- Backend: "Let me write a script to generate realistic data"
- Result: 2-day delay, ongoing maintenance

**Test coverage gap**:
- Features requiring semantic data: **NOT TESTED**
- Bugs slip to production

**Team frustration**:
- QA feels blocked
- Developers feel like they're "just generating test data" instead of shipping features

---

---

# 2. WHAT YOU'RE DOING NOW (Current Approaches)

## The Four Common Approaches

Teams try to solve the "realistic test data" problem in 4 ways. All have tradeoffs.

---

## Approach 1: Manual Test Data Creation

### How It Works
1. QA manually creates 10-20 "golden" test users
2. Saves them in spreadsheet
3. Imports to database before each test run
4. Maintains spreadsheet as schema changes

### The Reality Check

**Pros**:
- ✅ Data is realistic (humans wrote it)
- ✅ Full control over content

**Cons**:
- ❌ Doesn't scale (10-20 users max, not 1,000)
- ❌ Brittle (every schema change = update spreadsheet)
- ❌ Not CI/CD friendly (can't run in pipelines)
- ❌ Becomes stale (drifts from production reality)
- ❌ Team bottleneck (QA becomes data curator)

**Verdict**: Good for edge cases, bad for volume

---

## Approach 2: Random Generators (`rand:*` helpers)

### How It Works
```yaml
body: |
  {
    "id": "{{rand:guid()}}",
    "name": "{{rand:text(10, 'alpha')}}",
    "email": "{{rand:text(8, 'alnum')}}@test.com",
    "bio": "{{rand:text(100, 'alpha')}}",
    "age": {{rand:number(18, 65)}}
  }
```

### The Output
```json
{
  "id": "a3f2b5c8-e4d1-4f9a-b2c7-8e5f3a1d9c6b",
  "name": "Kdjfhaksld",
  "email": "hgkdjfla@test.com",
  "bio": "Dfkjghkdfhgkljsdhfgkljshdfglkjhsdflgkjhsdfg...",
  "age": 42
}
```

### The Reality Check

**Pros**:
- ✅ Fast (milliseconds per record)
- ✅ Scalable (generate millions)
- ✅ Inline (no external dependencies)
- ✅ Schema-valid (passes validation)

**Cons**:
- ❌ Semantically useless (can't test real features)
- ❌ Unrealistic (no human named "Kdjfhaksld")
- ❌ Breaks personalization features
- ❌ Breaks search/filter features
- ❌ Breaks recommendation engines

**Verdict**: Perfect for IDs/timestamps, terrible for content

---

## Approach 3: Python + Faker Library

### How It Works
```python
from faker import Faker
import json

fake = Faker()

users = []
for i in range(1000):
    users.append({
        "id": fake.uuid4(),
        "name": fake.name(),
        "email": fake.email(),
        "bio": fake.text(max_nb_chars=100),
        "age": fake.random_int(18, 65),
        "interests": [fake.word() for _ in range(3)]
    })

with open('users.json', 'w') as f:
    json.dump(users, f)
```

### The Output
```json
{
  "id": "a3f2b5c8-e4d1-4f9a-b2c7-8e5f3a1d9c6b",
  "name": "John Smith",
  "email": "john.smith@example.com",
  "bio": "Prepare foot indeed. Standard million...",
  "age": 42,
  "interests": ["foot", "standard", "million"]
}
```

### The Reality Check

**Pros**:
- ✅ Realistic names (actual person names)
- ✅ Realistic emails (proper format)
- ✅ Scalable (generate thousands)

**Cons**:
- ⚠️ Bio is word salad ("Prepare foot indeed")
- ❌ Interests are random words ("foot", "standard")
- ❌ External dependency (Python runtime, Faker lib)
- ❌ Separate workflow (generate → import → test)
- ❌ Maintenance overhead (update script on schema change)
- ❌ Not inline (can't generate on-the-fly)

**Verdict**: Better than random strings, still not realistic enough

---

## Approach 4: Production Data Anonymization

### How It Works
1. Export production database dump
2. Run anonymization script (remove PII)
3. Import anonymized data to staging
4. Use for testing

### The Reality Check

**Pros**:
- ✅ 100% realistic (was production data)
- ✅ Real distributions (age, geography, interests)

**Cons**:
- ❌ Privacy risks (incomplete anonymization)
- ❌ Compliance issues (GDPR, data residency)
- ❌ Large datasets (slow export/import)
- ❌ Becomes stale (production evolves, staging doesn't)
- ❌ Not repeatable (can't generate fresh on-demand)
- ❌ Requires production access (security concern)

**Verdict**: Realistic but risky and unmaintainable

---

## The Comparison Matrix

| Approach | Realistic | Scalable | Inline | Maintenance | CI/CD |
|----------|-----------|----------|--------|-------------|-------|
| Manual creation | ✅ | ❌ | ❌ | ❌ | ❌ |
| rand:text() | ❌ | ✅ | ✅ | ✅ | ✅ |
| Python + Faker | ⚠️ | ✅ | ❌ | ❌ | ⚠️ |
| Prod anonymization | ✅ | ❌ | ❌ | ❌ | ❌ |

---

### 📊 **Quick Summary**

**What we've tried**:
- Manual creation (doesn't scale)
- Random generators (not realistic)
- Python scripts (word salad)
- Production dumps (risky)

**What we need**:
- ✅ Realistic content
- ✅ Scalable generation
- ✅ Inline in workflows
- ✅ Low maintenance
- ✅ CI/CD native

**Next**: The LLM solution that delivers all 5...

---

---

# 3. THE LLM SOLUTION

## The Idea

**Use an LLM stage inline in your workflow to generate contextually realistic test data.**

### High-Level Flow
```
Workflow:
├─ Stage 1 (LLM): Generate realistic user profile
├─ Stage 2 (API): Create user via API
└─ Stage 3 (API): Verify user created
```

### Why It Works
- ✅ **Contextually realistic**: LLM understands semantic relationships
- ✅ **Inline**: No external scripts, part of workflow
- ✅ **Scalable**: Combine with `forEach` for bulk generation
- ✅ **Low maintenance**: Change prompt, not code
- ✅ **CI/CD native**: Runs in pipelines like any other stage

---

## Example: Generate 1 Realistic User

### Workflow
```yaml
version: "3.11"
name: "generate-realistic-user"
output: true

references:
  apis:
    - name: "users"
      definition: "users"

input:
  - name: "openaiApiKey"
    type: "Text"
    required: true
    secret: true
  - name: "jwt"
    type: "Text"
    required: true
    secret: true

stages:
  - name: "generate-profile"
    kind: "LLM"
    expectedStatus: 200
    config:
      connectionRef: "openai-main"
      model: "gpt-4o-mini"
      prompts:
        system:
          text: >
            You generate realistic user profiles for an e-commerce platform.
            Return only valid JSON. Do not include markdown fences or commentary.
        input:
          text: |
            Generate a realistic user profile with:
            - Full name (first + last)
            - Email address
            - Bio (50-100 words, realistic hobbies/interests)
            - Age (18-65)
            - 3-5 interests (realistic hobbies)
        output:
          text: "Return only JSON matching the configured schema."
      generation:
        temperature: 0.7
        responseFormat: schema
      output:
        schemaName: "user_profile"
        schemaStrict: true
        schema:
          type: object
          required: [name, email, bio, age, interests]
          properties:
            name:
              type: string
            email:
              type: string
            bio:
              type: string
            age:
              type: integer
            interests:
              type: array
              items:
                type: string
      limits:
        maxOutputTokens: 500
        timeoutSeconds: 45
    output:
      name: "{{response.body.output.json.name}}"
      email: "{{response.body.output.json.email}}"
      bio: "{{response.body.output.json.bio}}"
      age: "{{response.body.output.json.age}}"
      interests: "{{response.body.output.json.interests}}"
      inputTokens: "{{response.body.usage.inputTokens}}"
      outputTokens: "{{response.body.usage.outputTokens}}"
      totalTokens: "{{response.body.usage.totalTokens}}"
        
  - name: "create-user"
    kind: "Endpoint"
    apiRef: "users"
    endpoint: "/api/users"
    httpVerb: "POST"
    expectedStatuses: [200, 201]
    headers:
      Content-Type: application/json
      Authorization: Bearer {{input.jwt}}
    body: |
      {
        "id": "{{rand:guid()}}",
        "name": "{{stage:generate-profile.output.name}}",
        "email": "{{stage:generate-profile.output.email}}",
        "bio": "{{stage:generate-profile.output.bio}}",
        "age": {{stage:generate-profile.output.age}},
        "interests": {{stage:generate-profile.output.interests}}
      }
    output:
      userId: "{{response.body.id}}"

endStage:
  output:
    userId: "{{stage:create-user.output.userId}}"
    name: "{{stage:generate-profile.output.name}}"
    interests: "{{stage:generate-profile.output.interests}}"
    llmTotalTokens: "{{stage:generate-profile.output.totalTokens}}"
```

### Result
```json
{
  "id": "a3f2b5c8-e4d1-4f9a-b2c7-8e5f3a1d9c6b",
  "name": "Sarah Chen",
  "email": "sarah.chen.1991@gmail.com",
  "bio": "Marketing professional passionate about sustainable fashion and mindful living. Weekends you'll find me at yoga class or exploring local farmers markets. Currently learning pottery and always planning my next travel adventure.",
  "age": 32,
  "interests": ["sustainable fashion", "yoga", "pottery", "travel", "farmers markets"]
}
```

### Compare: Random vs LLM-Generated

| Field | rand:text() | LLM-Generated |
|-------|-------------|---------------|
| Name | "Kdjfhaksld" | "Sarah Chen" |
| Email | "hgkdj@test.com" | "sarah.chen.1991@gmail.com" |
| Bio | "Dfkjghkdfhg..." | "Marketing professional passionate about..." |
| Interests | ["Qwerty", "Asdfgh"] | ["sustainable fashion", "yoga", "pottery"] |

**Impact**: QA can now test recommendation engine, search, personalization, UI/UX with realistic data.

---

## Example: Generate Users in Bulk

### Workflow with forEach
```yaml
version: "3.11"
name: "bulk-generate-users"
output: true

references:
  apis:
    - name: "users"
      definition: "users"

input:
  - name: "openaiApiKey"
    type: "Text"
    required: true
    secret: true
  - name: "jwt"
    type: "Text"
    required: true
    secret: true
  - name: "batchSize"
    type: "Number"
    required: true

stages:
  - name: "generate-profiles"
    kind: "LLM"
    expectedStatus: 200
    config:
      connectionRef: "openai-main"
      model: "gpt-4o-mini"
      prompts:
        system:
          text: >
            Generate diverse, realistic, fictional e-commerce user profiles.
            Return only valid JSON. No markdown fences, no extra commentary.
        input:
          text: |
            Generate exactly {{input.batchSize}} user profiles.
            Vary demographics, interests, backgrounds, professions, and writing style.
            Each profile must include name, email, bio, age, and 3-5 realistic interests.
        output:
          text: "Return only JSON matching the configured schema."
      generation:
        temperature: 0.8
        responseFormat: schema
      output:
        schemaName: "user_profile_list"
        schemaStrict: true
        schema:
          type: object
          required: [users]
          properties:
            users:
              type: array
              minItems: 1
              items:
                type: object
                required: [name, email, bio, age, interests]
                properties:
                  name: { type: string }
                  email: { type: string }
                  bio: { type: string }
                  age: { type: integer, minimum: 18, maximum: 65 }
                  interests:
                    type: array
                    minItems: 3
                    maxItems: 5
                    items: { type: string }
      limits:
        maxOutputTokens: 2500
        timeoutSeconds: 60
    output:
      users: "{{response.body.output.json.users}}"
      totalTokens: "{{response.body.usage.totalTokens}}"
        
  - name: "create-users"
    kind: "Endpoint"
    apiRef: "users"
    endpoint: "/api/users"
    httpVerb: "POST"
    expectedStatuses: [200, 201]
    forEach: "{{stage:generate-profiles.output.users}}"
    itemName: "user"
    indexName: "userIndex"
    headers:
      Content-Type: application/json
      Authorization: Bearer {{input.jwt}}
    body: |
      {
        "id": "{{rand:guid()}}",
        "name": "{{context:user.name}}",
        "email": "{{context:user.email}}",
        "bio": "{{context:user.bio}}",
        "age": {{context:user.age}},
        "interests": {{context:user.interests}},
        "seedIndex": {{context:userIndex}},
        "createdAt": "{{system:datetime.utcnow}}"
      }
    output:
      userId: "{{response.body.id}}"

endStage:
  output:
    generatedCount: "{{stage:generate-profiles.output.users.length?}}"
    createdCount: "{{stage:create-users.output.foreach_count}}"
    createdUsers: "{{stage:create-users.output.foreach_items}}"
    llmTotalTokens: "{{stage:generate-profiles.output.totalTokens}}"
```

**Execute**:
```bash
sih --workflow bulk-generate-users.workflow \
    --env staging \
    --varsfile bulk-generate-users.wfvars
```

**Result**: A realistic batch is generated by the LLM and then seeded through your API with `forEach`.

---

## Cost Analysis

### LLM Cost (OpenAI GPT-4o-mini)
- Input: ~100 tokens/user (prompt)
- Output: ~150 tokens/user (profile)
- Total: ~250 tokens/user in this simplified estimate
- Provider pricing changes over time, so treat the following as an illustrative calculation, not a tool guarantee

**1,000 users**:
- Input: 100,000 tokens
- Output: 150,000 tokens
- **Illustrative total**: usually small for compact batches, but calculate it with your current provider/model pricing before committing it to a CI budget.

### Alternative Cost (Developer Time)
- Write Python + Faker script: 4 hours
- Maintain interest taxonomies: 2 hours/month
- Update when schema changes: 1 hour/change
- **Developer cost**: $200-400 initial + $100-200/month ongoing

**ROI**: The LLM approach can be cheaper in developer time when the alternative is building and maintaining a custom semantic data generator.

---

---

# 4. IMPLEMENTATION DEEP DIVE

## Prerequisites

### 1. Enable OpenAI Plugin
```yaml
# workflows.config
plugins:
  - http
  - openai
```

### 2. Configure Connection in Catalog
```yaml
# api.catalog
- version: "3.11"
  plugins:
    - id: openai
      contractVersion: "1.0"
      runtimeVersion: "1.0"
  connections:
    - name: "openai-main"
      type: llm
      provider: openai
      baseUrl:
        local: https://api.openai.com/v1
      apiKeySecret: "{{input.openaiApiKey}}"
      config:
        model: "gpt-4o-mini"
```

### 3. Provide the API Key through `.wfvars`
```yaml
local:
  openaiApiKey: "replace-with-openai-api-key"
```

---

## LLM Stage Configuration

### Basic Structure
```yaml
- name: "stage-name"
  kind: "LLM"
  config:
    connectionRef: "openai-main"
    model: "gpt-4o-mini"
    prompts:
      system:
        text: "System prompt here"
      input:
        text: "User prompt here"
      output:
        text: "Return only JSON matching the configured schema."
    generation:
      responseFormat: schema
    output:
      schemaName: "output_name"
      schema: { ... }
```

### Prompt Design Best Practices

#### ✅ **Good Prompts** (Specific, Constrained)
```yaml
prompts:
  system:
    text: "Generate realistic user profiles for an e-commerce platform"
  input:
    text: |
      Create a unique user profile with:
      - Full name (diverse backgrounds)
      - Professional email
      - Bio: 50-100 words about hobbies, profession, interests
      - Age: 18-65
      - 3-5 realistic interests (hobbies, not generic words)
      
      Vary demographics, professions, and interests for diversity.
```

#### ❌ **Bad Prompts** (Too Vague)
```yaml
prompts:
  system:
    text: "Generate user data"
  input:
    text: "Create a user"
```

**Why bad**: LLM might generate inconsistent formats, missing fields, or unrealistic data.

---

### Output Schema (Strict Mode)

**Use `schemaStrict: true` with schema output** when you want the model response constrained to the expected shape:

```yaml
output:
  schemaName: "user_profile"
  schemaStrict: true
  schema:
    type: object
    required: [name, email, bio, age, interests]
    properties:
      name:
        type: string
        description: "Full name (first + last)"
      email:
        type: string
        description: "Professional email address"
      bio:
        type: string
        description: "50-100 word bio"
      age:
        type: integer
        minimum: 18
        maximum: 65
      interests:
        type: array
        minItems: 3
        maxItems: 5
        items:
          type: string
```

**Benefits**:
- ✅ Stronger schema compliance from the provider
- ✅ Type safety (no string where integer expected)
- ✅ Validation at LLM level (before API call)

---

### Token Limits

```yaml
limits:
  maxInputTokens: 1000    # Prompt size
  maxOutputTokens: 500    # Response size
  maxTotalTokens: 1500    # Combined
  timeoutSeconds: 30      # Max wait time
```

**Best practices**:
- Set `maxOutputTokens` based on expected response size
- Set `timeoutSeconds` for long-running generations
- Monitor token usage in execution reports

---

### Reasoning Effort

For models that support reasoning controls:

```yaml
config:
  reasoning:
    effort: "low"  # low | medium | high
```

**When to use**:
- `low`: Simple data generation (user profiles)
- `medium`: Complex validation logic
- `high`: Multi-step reasoning (not needed for test data)

---

## Advanced Patterns

### Pattern 1: Context-Aware Generation

**Use previous stage outputs to inform LLM**:

```yaml
stages:
  - name: "get-product-categories"
    kind: "Endpoint"
    endpoint: "/api/categories"
    output:
      categories: "{{response.body}}"
      
  - name: "generate-user-with-relevant-interests"
    kind: "LLM"
    config:
      connectionRef: "openai-main"
      model: "gpt-4o-mini"
      prompts:
        system:
          text: "Generate realistic e-commerce user profiles as JSON."
        input:
          text: |
            Generate a user profile with interests related to these product categories:
            {{stage:get-product-categories.output.categories}}
            
            Ensure interests are realistic hobbies that align with the categories.
        output:
          text: "Return only JSON matching the configured schema."
```

**Result**: Generated users have interests that match your product catalog.

---

### Pattern 2: Conditional Generation

**Generate different types of users based on input**:

```yaml
input:
  - name: "userType"
    type: "Text"  # "premium" | "basic"

stages:
  - name: "generate-profile"
    kind: "LLM"
    config:
      connectionRef: "openai-main"
      model: "gpt-4o-mini"
      prompts:
        system:
          text: "Generate realistic user profiles as JSON."
        input:
          text: |
            Generate a {{input.userType}} user profile:

            If userType is premium:
            - Prefer professional backgrounds such as executive, founder, consultant, or senior specialist
            - Prefer age range 35-55
            - Include premium interests such as golf, wine, design hotels, fine dining, or luxury travel

            If userType is basic:
            - Use varied backgrounds
            - Age 18-65
            - Diverse interests
        output:
          text: "Return only JSON matching the configured schema."
```

---

### Pattern 3: Batch Generation with Diversity

**Ensure diverse profiles in bulk generation**:

```yaml
- name: "generate-diverse-users"
  kind: "LLM"
  config:
    connectionRef: "openai-main"
    model: "gpt-4o-mini"
    prompts:
      system:
        text: "Generate realistic, fictional user profiles as JSON."
      input:
        text: |
          Generate 10 diverse user profiles:
          - Vary age, gender, ethnicity, profession
          - Different geographic locations (urban/rural, various countries)
          - Mix of interests (sports, arts, tech, outdoor, creative)
          - Ensure no two profiles are too similar
      output:
        text: "Return only JSON matching the configured schema."
    output:
      schema:
        type: object
        required: [users]
        properties:
          users:
            type: array
            minItems: 10
            maxItems: 10
            items:
              type: object
              # user schema
```

**Why batches of 10**: Often cheaper and simpler than making 10 individual LLM calls. Then use `forEach: "{{stage:generate-diverse-users.output.users}}"` in the API stage.

---

---

# 5. REAL-WORLD USE CASES

## Use Case 1: E-Commerce Recommendation Testing

### The Challenge
**Feature**: Product recommendation engine based on user interests.

**Problem**: Can't test with random data.
- User interests: ["Qwerty", "Asdfgh"]
- Products: "Hiking boots", "Camera lens", "Yoga mat"
- Algorithm: ??? (no semantic match)

### The Solution
```yaml
- name: "generate-shoppers"
  kind: "LLM"
  config:
    connectionRef: "openai-main"
    model: "gpt-4o-mini"
    prompts:
      system:
        text: "Generate realistic online shopper profiles as JSON."
      input:
        text: |
          Create a batch of realistic shopper profiles with:
          - Realistic shopping interests (fashion, tech, home, sports, etc.)
          - Age and demographic info
          - Shopping behavior hints in bio
      output:
        text: "Return only JSON matching the configured schema."
    output:
      schema:
        # object with users array
```

### Result
```json
{
  "name": "Marcus Johnson",
  "interests": ["running", "fitness tech", "outdoor gear", "nutrition"],
  "bio": "Marathon runner and tech enthusiast. Always looking for the latest running gadgets..."
}
```

**Impact**: Recommendation engine now returns relevant products:
- "Running shoes" ✅
- "Fitness tracker" ✅
- "Protein powder" ✅

---

## Use Case 2: Social Media Platform Moderation Testing

### The Challenge
**Feature**: Content moderation algorithm (policy categories, spam detection, and borderline content).

**Problem**: Need realistic, labeled posts to test false positive/negative rates without putting unsafe text directly into fixtures.

### The Solution
```yaml
- name: "generate-test-posts"
  kind: "LLM"
  config:
    connectionRef: "openai-main"
    model: "gpt-4o-mini"
    prompts:
      system:
        text: "Generate policy-labeled social media test fixtures as JSON."
      input:
        text: |
          Create a batch of diverse social media test posts:
          - 80% normal posts (hobbies, daily life, questions)
          - 15% borderline content (controversial but not violating)
          - 5% policy-violating placeholders using labels, not explicit harmful text
          
          Label each post: "normal" | "borderline" | "policy_violation_placeholder"
      output:
        text: "Return only JSON matching the configured schema."
    output:
      schema:
        type: object
        required: [posts]
        properties:
          posts:
            type: array
            items:
              type: object
              properties:
                text: {type: string}
                label: {type: string, enum: [normal, borderline, policy_violation_placeholder]}
```

### Result
Generated posts include:
- Normal: "Just finished a great book! Any recommendations?"
- Borderline: "Tired of politicians not listening to us..."
- Policy placeholder: "[policy violation placeholder: spam]"

**Impact**: Moderation algorithm tested with labeled ground truth.

---

## Use Case 3: Customer Support Chatbot Training

### The Challenge
**Feature**: AI chatbot for customer support.

**Problem**: Need realistic customer queries to test response quality.

### The Solution
```yaml
- name: "generate-support-queries"
  kind: "LLM"
  config:
    connectionRef: "openai-main"
    model: "gpt-4o-mini"
    prompts:
      system:
        text: "Generate realistic customer support queries as JSON."
      input:
        text: |
          Create a batch of customer support queries for an e-commerce platform:
          - Vary urgency (low/medium/high)
          - Vary category (shipping, returns, product info, account)
          - Use realistic customer language (not formal)
          - Include emotional tone where appropriate
      output:
        text: "Return only JSON matching the configured schema."
    output:
      schema:
        type: object
        required: [queries]
        properties:
          queries:
            type: array
            items:
              type: object
              properties:
                query: {type: string}
                category: {type: string}
                urgency: {type: string}
```

### Result
```json
{
  "query": "Where is my order?? I ordered 5 days ago and tracking says 'pending' still. I need it by Friday!",
  "category": "shipping",
  "urgency": "high"
}
```

**Impact**: Chatbot tested with realistic query patterns, emotional tones.

---

## Use Case 4: Analytics Dashboard Testing

### The Challenge
**Feature**: Analytics dashboard showing user demographics, interests distribution.

**Problem**: Random data produces meaningless charts.

### The Solution
Generate realistic demographic distributions:

```yaml
- name: "generate-demographic-data"
  kind: "LLM"
  config:
    connectionRef: "openai-main"
    model: "gpt-4o-mini"
    prompts:
      system:
        text: "Generate realistic demographic test data as JSON."
      input:
        text: |
          Generate a batch of user profiles following realistic demographic distributions:
          - Age: Normal distribution, mean=35, std=12
          - Geographic: 60% urban, 30% suburban, 10% rural
          - Interests: Follow realistic hobby distributions
          - Gender: Balanced
      output:
        text: "Return only JSON matching the configured schema."
```

### Result
Dashboard now shows:
- Age distribution: Bell curve (realistic)
- Top interests: "fitness" (18%), "travel" (15%), "cooking" (12%)
- Geographic split matches real-world patterns

**Impact**: Designers can validate dashboard UI with realistic data.

---

---

# 6. WHEN TO USE (AND NOT USE)

## ✅ Use LLM-Generated Test Data When:

### 1. **Semantic Features Under Test**
- Recommendation engines
- Search and filtering
- Content personalization
- Natural language processing

### 2. **UI/UX Validation**
- Profile cards, bios, timelines
- Content wrapping, truncation
- Realistic text lengths

### 3. **Analytics and Reporting**
- Demographic distributions
- Interest/behavior patterns
- Trend visualization

### 4. **Integration Testing**
- Multi-step workflows with realistic data flow
- End-to-end scenarios with contextual data

### 5. **Load Testing with Realistic Payloads**
- API performance with realistic request sizes
- Database queries with realistic data distributions

---

## ❌ Don't Use LLM-Generated Test Data When:

### 1. **Schema Validation Only**
```
If you just need to test:
- Field types (string, integer, boolean)
- Required/optional fields
- Max lengths, ranges

Use: rand:* helpers (faster, cheaper)
```

### 2. **High-Volume, Low-Value Data**
```
Examples:
- Session IDs
- Timestamps
- UUIDs
- Sequential integers

Use: rand:guid(), system:datetime.utcnow, or sequence variables
```

### 3. **Deterministic Test Cases**
```
If you need exact, reproducible test data:
- Regression tests with fixed inputs
- Edge case validation (null, empty, max values)

Use: Hardcoded fixtures, checked-in data files, or explicit edge-case payloads
```

### 4. **Performance-Critical Paths**
```
If seeding must be <1 second:
- LLM calls add 1-3 seconds per generation
- Use cached/pre-generated data instead
```

### 5. **Strict Budget Constraints**
```
If cost is primary concern:
- LLM: variable, depends on model, prompt, output size, and provider pricing
- rand:*: Free

Trade-off: Cost vs. realism
```

---

## Hybrid Approach (Recommended)

**Combine rand:* with LLM where it matters**:

```yaml
stages:
  - name: "create-users"
    forEach: "{{stage:generate-users.output.users}}"
    itemName: "user"
    body: |
      {
        "id": "{{rand:guid()}}",                    # Random (fast, cheap)
        "createdAt": "{{system:datetime.utcnow}}",  # System token
        "name": "{{context:user.name}}",            # LLM-generated (realistic)
        "email": "{{context:user.email}}",          # LLM-generated
        "bio": "{{context:user.bio}}",              # LLM-generated
        "interests": {{context:user.interests}}     # LLM-generated
      }
```

**Result**: 
- IDs, timestamps: Fast, cheap (rand:*)
- Semantic fields: Realistic (LLM)
- Best of both worlds

---

---

# 7. GETTING STARTED

## Step 1: Install SphereIntegrationHub

```bash
# Option 1: npm (no .NET required)
npm install -g @pinedatec.eu/sphere-integration-hub
sih --version

# Option 2: .NET tool
dotnet tool install -g SphereIntegrationHub.Tool
sih --version
```

---

## Step 2: Get OpenAI API Key

1. Go to https://platform.openai.com/api-keys
2. Create new API key
3. Put it in a `.wfvars` file as a secret workflow input.

Example `generate-user.wfvars`:

```yaml
local:
  openaiApiKey: "replace-with-openai-api-key"
  jwt: "replace-with-bearer-token"
  batchSize: 10
```

---

## Step 3: Configure Workflows

### Create `workflows.config`:
```yaml
plugins:
  - http
  - openai
```

### Create `api.catalog`:
```yaml
- version: "3.11"
  plugins:
    - id: openai
      contractVersion: "1.0"
      runtimeVersion: "1.0"
  connections:
    - name: "openai-main"
      type: llm
      provider: openai
      baseUrl:
        local: https://api.openai.com/v1
      apiKeySecret: "{{input.openaiApiKey}}"
      config:
        model: "gpt-4o-mini"
  definitions:
    - name: "my-api"
      contractType: openapi
      openApiUrl: /swagger/v1/swagger.json
      baseUrl:
        local: https://localhost:5000
```

---

## Step 4: Create Your First LLM Workflow

### `generate-user.workflow`:
```yaml
version: "3.11"
name: "generate-realistic-user"
output: true

references:
  apis:
    - name: "my-api"
      definition: "my-api"

input:
  - name: "openaiApiKey"
    type: "Text"
    required: true
    secret: true
  - name: "jwt"
    type: "Text"
    required: true
    secret: true

stages:
  - name: "generate-profile"
    kind: "LLM"
    expectedStatus: 200
    config:
      connectionRef: "openai-main"
      model: "gpt-4o-mini"
      prompts:
        system:
          text: >
            Generate realistic user profiles for an e-commerce platform.
            Return only valid JSON. No markdown fences, no extra commentary.
        input:
          text: |
            Create a unique user profile with:
            - Full name (diverse backgrounds)
            - Professional email
            - Bio: 50-100 words about hobbies and interests
            - Age: 18-65
            - 3-5 realistic interests
        output:
          text: "Return only JSON matching the configured schema."
      generation:
        temperature: 0.7
        responseFormat: schema
      output:
        schemaName: "user_profile"
        schemaStrict: true
        schema:
          type: object
          required: [name, email, bio, age, interests]
          properties:
            name: {type: string}
            email: {type: string}
            bio: {type: string}
            age: {type: integer, minimum: 18, maximum: 65}
            interests:
              type: array
              minItems: 3
              maxItems: 5
              items: {type: string}
      limits:
        maxOutputTokens: 500
        timeoutSeconds: 30
    output:
      name: "{{response.body.output.json.name}}"
      email: "{{response.body.output.json.email}}"
      bio: "{{response.body.output.json.bio}}"
      age: "{{response.body.output.json.age}}"
      interests: "{{response.body.output.json.interests}}"
      totalTokens: "{{response.body.usage.totalTokens}}"
      
  - name: "create-user"
    kind: "Endpoint"
    apiRef: "my-api"
    endpoint: "/api/users"
    httpVerb: "POST"
    expectedStatuses: [200, 201]
    headers:
      Content-Type: application/json
      Authorization: Bearer {{input.jwt}}
    body: |
      {
        "id": "{{rand:guid()}}",
        "name": "{{stage:generate-profile.output.name}}",
        "email": "{{stage:generate-profile.output.email}}",
        "bio": "{{stage:generate-profile.output.bio}}",
        "age": {{stage:generate-profile.output.age}},
        "interests": {{stage:generate-profile.output.interests}}
      }
    output:
      userId: "{{response.body.id}}"

endStage:
  output:
    userId: "{{stage:create-user.output.userId}}"
    name: "{{stage:generate-profile.output.name}}"
    email: "{{stage:generate-profile.output.email}}"
    interests: "{{stage:generate-profile.output.interests}}"
    llmTotalTokens: "{{stage:generate-profile.output.totalTokens}}"
```

---

## Step 5: Execute

```bash
sih --workflow generate-user.workflow \
    --env local \
    --varsfile generate-user.wfvars \
    --report-format both
```

**Output**:
```
Preflight checks:
✓ API: my-api (https://localhost:5000) - Ready
✓ Connection: openai-main (https://api.openai.com/v1) - Ready

Executing workflow: generate-realistic-user
├─ [1/2] generate-profile (LLM) ... 2.3s [Ok]
│   └─ Tokens: 120 input, 180 output (300 total)
└─ [2/2] create-user ............... 95ms [Ok]

Final output:
  userId: "a3f2b5c8-e4d1-4f9a-b2c7-8e5f3a1d9c6b"
  name: "Sarah Chen"
  email: "sarah.chen.1991@gmail.com"
  interests: ["sustainable fashion", "yoga", "pottery"]
  llmTotalTokens: 300
```

---

## Step 6: Scale to Bulk Generation

### Modify workflow for batch generation + forEach:

```yaml
input:
  - name: "batchSize"
    type: "Number"
    required: true

stages:
  - name: "generate-profiles"
    kind: "LLM"
    config:
      # Generate exactly {{input.batchSize}} profiles
    output:
      users: "{{response.body.output.json.users}}"
      
  - name: "create-users"
    kind: "Endpoint"
    forEach: "{{stage:generate-profiles.output.users}}"
    itemName: "user"
    body: |
      {
        "id": "{{rand:guid()}}",
        "name": "{{context:user.name}}",
        "email": "{{context:user.email}}"
        # ...
      }
```

**Execute**:
```bash
sih --workflow generate-user.workflow \
    --env local \
    --varsfile generate-user.wfvars
```

**Result**: A realistic batch is generated by the LLM, then `forEach` seeds each user through the API.

---

---

# CONCLUSION

## The Shift

**From**:
```yaml
"name": "{{rand:text(10, 'alpha')}}"  # → "Kdjfhaksld"
```

**To**:
```yaml
"name": "{{stage:generate-profile.output.name}}"  # → "Sarah Chen"
```

---

## The Real Impact

Test data isn't about filling databases.

It's about **validating behavior**.

And you can't validate behavior with users named "Kdjfhaksld" interested in "Qwerty".

---

## Your Tests Pass Because They Test Nothing Real

Random data validates **schema**.

Realistic data validates **systems**.

The recommendation engine doesn't break because the data type is wrong.

It breaks because nobody tested it with users who actually like "photography" instead of "Asdfgh".

---

## The Choice

With a small model and compact prompts, generating realistic profiles can cost cents to low dollars, depending on current provider pricing and output size.

A few hours of custom script work can easily cost more than that, and the script still needs maintenance when your schema changes.

The math is not universal, but the tradeoff is clear: spend engineering time maintaining fake-data scripts, or let the workflow generate semantic test data where it matters.

---

## What You Get

1. ✅ **Realistic content** (not word salad)
2. ✅ **Inline generation** (no external scripts)
3. ✅ **Scalable in batches** (generate arrays, then seed with `forEach`)
4. ✅ **Predictable controls** (schema output, token limits, timeout, mocks)
5. ✅ **Better test coverage** (semantic features now testable)

---

## Resources

- **GitHub**: [github.com/PinedaTec-EU/SphereIntegrationHub](https://github.com/PinedaTec-EU/SphereIntegrationHub)
- **Documentation**: [OpenAI Plugin Guide](https://github.com/PinedaTec-EU/SphereIntegrationHub/blob/main/.doc/plugins-openai.md)
- **NPM**: `@pinedatec.eu/sphere-integration-hub`
- **NuGet**: `SphereIntegrationHub.Tool`

---

## Next Steps

1. Install SphereIntegrationHub
2. Get OpenAI API key
3. Create your first LLM workflow
4. Generate 10 users (test)
5. Scale to 1,000+ users (bulk)
6. Test features that were previously untestable

---

## Stop Generating Noise. Start Generating Reality.

---

**About the Author**

Jose Manuel Rodriguez Pineda is a backend developer specializing in API orchestration and developer tooling.

Connect: [LinkedIn](https://www.linkedin.com/in/jmrpineda) | Email: sih@pinedatec.eu

---

*Tags: #dotnet #csharp #testing #llm #openai #testdata #qa #devops #ai*

---

---

# CANVAS METADATA

**Status**: Draft  
**Target Publication**: C# Corner  
**Estimated Length**: ~6,000 words  
**Reading Time**: ~24 minutes  
**Code Examples**: 15+  
**Target Completion**: TBD

**Key Differentiators**:
- Pain-driven narrative (not feature-first)
- Real QA scenario (relatable)
- Cost analysis (ROI clear)
- Hybrid approach (not all-or-nothing)
- Production-ready examples (copy-paste)

**Next Steps**:
- [ ] Review pain stories (are they compelling?)
- [ ] Validate code examples (are they accurate?)
- [ ] Add screenshots (execution reports, generated data)
- [ ] Refine use cases (cover more scenarios?)
- [ ] Optimize length (too long? cut sections?)
