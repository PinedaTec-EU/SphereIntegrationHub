# Java SDK

Recommended package:

- `eu.pinedatec.sphereintegrationhub`

Recommended entry point:

```java
import eu.pinedatec.sphereintegrationhub.sih;
import eu.pinedatec.sphereintegrationhub.sihub;
```

Recommended usage:

```java
WorkflowRunResult result = sihub
    .run("./workflows/create-account.workflow")
    .environment("local")
    .input("email", "john@doe.com")
    .varsFile("./workflows/create-account.wfvars")
    .execute();

String accountId = result.output().get("accountId");
```

Suggested public API:

```java
package eu.pinedatec.sphereintegrationhub;

import java.nio.file.Path;
import java.util.Map;

public record WorkflowRunResult(
    Map<String, String> output,
    String workflowPath,
    String environment,
    String catalogVersion,
    String catalogPath,
    String varsFilePath,
    String outputFilePath,
    String jsonReportPath,
    String htmlReportPath,
    String executionId) {
}

public final class WorkflowRunBuilder {
    public WorkflowRunBuilder environment(String environment) { ... }
    public WorkflowRunBuilder catalog(String catalogPath) { ... }
    public WorkflowRunBuilder envFile(String envFilePath) { ... }
    public WorkflowRunBuilder varsFile(String varsFilePath) { ... }
    public WorkflowRunBuilder input(String key, String value) { ... }
    public WorkflowRunBuilder inputs(Map<String, String> inputs) { ... }
    public WorkflowRunBuilder mocked(boolean enabled) { ... }
    public WorkflowRunBuilder verbose(boolean enabled) { ... }
    public WorkflowRunBuilder debug(boolean enabled) { ... }
    public WorkflowRunBuilder refreshCache(boolean enabled) { ... }
    public WorkflowRunResult execute() { ... }
}

public final class sihub {
    public static WorkflowRunBuilder run(String workflowPath) { ... }
}

public final class sih {
    public static WorkflowRunBuilder run(String workflowPath) { ... }
}
```

Notes:

- keep the Java API minimal and runtime-host oriented
- `api.catalog` should resolve automatically from the workflow location by default
- `catalog(...)` remains an override path
- avoid adding annotation-heavy or generated endpoint clients into this package; that would be a different concern from workflow execution
