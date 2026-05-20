# Go SDK

Recommended module:

- `github.com/PinedaTec-EU/SphereIntegrationHub/sdk/go/sih`

Recommended entry point:

```go
import "github.com/PinedaTec-EU/SphereIntegrationHub/sdk/go/sih"
```

Recommended usage:

```go
result, err := sih.Run("./workflows/create-account.workflow").
    Environment("local").
    Input("email", "john@doe.com").
    VarsFile("./workflows/create-account.wfvars").
    Execute(ctx)
if err != nil {
    return err
}

accountID := result.Output["accountId"]
```

Suggested public API:

```go
package sih

import "context"

type WorkflowRunResult struct {
    Output         map[string]string
    WorkflowPath   string
    Environment    string
    CatalogVersion string
    CatalogPath    *string
    VarsFilePath   *string
    OutputFilePath *string
    JsonReportPath *string
    HtmlReportPath *string
    ExecutionID    *string
}

type WorkflowRunBuilder interface {
    Environment(name string) WorkflowRunBuilder
    Catalog(path string) WorkflowRunBuilder
    EnvFile(path string) WorkflowRunBuilder
    VarsFile(path string) WorkflowRunBuilder
    Input(key, value string) WorkflowRunBuilder
    Inputs(values map[string]string) WorkflowRunBuilder
    Mocked(enabled bool) WorkflowRunBuilder
    Verbose(enabled bool) WorkflowRunBuilder
    Debug(enabled bool) WorkflowRunBuilder
    RefreshCache(enabled bool) WorkflowRunBuilder
    Execute(ctx context.Context) (*WorkflowRunResult, error)
}

func Run(workflowPath string) WorkflowRunBuilder
```

Notes:

- keep the Go API small and explicit
- default catalog resolution should follow the workflow path just like the other SDKs
- `Catalog(...)` remains an override
- if the first implementation shells out to `sih`, hide that detail behind the builder and keep the exported contract stable
