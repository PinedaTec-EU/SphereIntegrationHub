# .NET SDK

Current project:

- `src/SphereIntegrationHub.SDK.DotNet`

Current entry point:

```csharp
using SphereIntegrationHub.Sdk;
```

Recommended usage:

```csharp
var result = await sihub
    .Run("./workflows/create-account.workflow")
    .Environment("local")
    .Input("email", "john@doe.com")
    .VarsFile("./workflows/create-account.wfvars")
    .ExecuteAsync();

var accountId = result.Output["accountId"];
```

Current public shape:

```csharp
public static class sihub
{
    public static WorkflowRunBuilder Run(string workflowPath);
}

public static class sih
{
    public static WorkflowRunBuilder Run(string workflowPath);
}

public sealed class WorkflowRunBuilder
{
    public WorkflowRunBuilder Environment(string environment);
    public WorkflowRunBuilder Catalog(string catalogPath);
    public WorkflowRunBuilder Catalog(ApiCatalogVersion catalogVersion);
    public WorkflowRunBuilder EnvFile(string envFilePath);
    public WorkflowRunBuilder VarsFile(string varsFilePath);
    public WorkflowRunBuilder Input(string key, string value);
    public WorkflowRunBuilder Inputs(IReadOnlyDictionary<string, string> inputs);
    public WorkflowRunBuilder Mocked(bool enabled = true);
    public WorkflowRunBuilder Verbose(bool enabled = true);
    public WorkflowRunBuilder Debug(bool enabled = true);
    public WorkflowRunBuilder RefreshCache(bool enabled = true);
    public Task<WorkflowRunResult> ExecuteAsync(CancellationToken cancellationToken = default);
}

public sealed record WorkflowRunResult(
    IReadOnlyDictionary<string, string> Output,
    string WorkflowPath,
    string Environment,
    string CatalogVersion,
    string? CatalogPath,
    string? VarsFilePath,
    string? OutputFilePath,
    string? JsonReportPath,
    string? HtmlReportPath,
    string? ExecutionId);
```

Notes:

- `api.catalog` is resolved automatically from the workflow path by default
- `.wfvars` is resolved automatically from the workflow path by default
- `Catalog(...)` remains an override path for tests or embedded scenarios
- the SDK executes existing workflows and returns workflow-defined outputs only
