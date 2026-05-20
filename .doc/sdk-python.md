# Python SDK

Recommended package:

- `sphereintegrationhub`

Recommended entry point:

```python
from sphereintegrationhub import sihub, sih
```

Recommended usage:

```python
result = (
    sihub
    .run("./workflows/create-account.workflow")
    .environment("local")
    .input("email", "john@doe.com")
    .vars_file("./workflows/create-account.wfvars")
    .execute()
)

account_id = result.output["accountId"]
```

Suggested public API:

```python
from dataclasses import dataclass
from typing import Mapping


@dataclass(frozen=True)
class WorkflowRunResult:
    output: Mapping[str, str]
    workflow_path: str
    environment: str
    catalog_version: str
    catalog_path: str | None = None
    vars_file_path: str | None = None
    output_file_path: str | None = None
    json_report_path: str | None = None
    html_report_path: str | None = None
    execution_id: str | None = None


class WorkflowRunBuilder:
    def environment(self, name: str) -> "WorkflowRunBuilder": ...
    def catalog(self, path_or_catalog) -> "WorkflowRunBuilder": ...
    def env_file(self, path: str) -> "WorkflowRunBuilder": ...
    def vars_file(self, path: str) -> "WorkflowRunBuilder": ...
    def input(self, key: str, value: str) -> "WorkflowRunBuilder": ...
    def inputs(self, values: Mapping[str, str]) -> "WorkflowRunBuilder": ...
    def mocked(self, enabled: bool = True) -> "WorkflowRunBuilder": ...
    def verbose(self, enabled: bool = True) -> "WorkflowRunBuilder": ...
    def debug(self, enabled: bool = True) -> "WorkflowRunBuilder": ...
    def refresh_cache(self, enabled: bool = True) -> "WorkflowRunBuilder": ...
    def execute(self) -> WorkflowRunResult: ...


class _SihubStatic:
    def run(self, workflow_path: str) -> "WorkflowRunBuilder": ...


sihub: _SihubStatic
sih: _SihubStatic
```

Notes:

- use snake_case names for Python-native ergonomics
- preserve the same semantics as the TypeScript and .NET SDKs
- `catalog(...)` remains an override path
- first implementation may wrap the CLI if needed, but must not expose CLI-shaped arguments directly as the primary API
