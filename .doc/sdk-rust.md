# Rust SDK

Recommended crate:

- `sphere_integration_hub`

Recommended entry point:

```rust
use sphere_integration_hub::{sih, sihub};
```

Recommended usage:

```rust
let result = sihub::run("./workflows/create-account.workflow")
    .environment("local")
    .input("email", "john@doe.com")
    .vars_file("./workflows/create-account.wfvars")
    .execute()
    .await?;

let account_id = &result.output["accountId"];
```

Suggested public API:

```rust
use std::collections::BTreeMap;
use std::path::Path;

pub struct WorkflowRunResult {
    pub output: BTreeMap<String, String>,
    pub workflow_path: String,
    pub environment: String,
    pub catalog_version: String,
    pub catalog_path: Option<String>,
    pub vars_file_path: Option<String>,
    pub output_file_path: Option<String>,
    pub json_report_path: Option<String>,
    pub html_report_path: Option<String>,
    pub execution_id: Option<String>,
}

pub struct WorkflowRunBuilder { /* omitted */ }

impl WorkflowRunBuilder {
    pub fn environment(self, name: impl Into<String>) -> Self;
    pub fn catalog_path(self, path: impl AsRef<Path>) -> Self;
    pub fn env_file(self, path: impl AsRef<Path>) -> Self;
    pub fn vars_file(self, path: impl AsRef<Path>) -> Self;
    pub fn input(self, key: impl Into<String>, value: impl Into<String>) -> Self;
    pub fn inputs<I, K, V>(self, values: I) -> Self
    where
        I: IntoIterator<Item = (K, V)>,
        K: Into<String>,
        V: Into<String>;
    pub fn mocked(self, enabled: bool) -> Self;
    pub fn verbose(self, enabled: bool) -> Self;
    pub fn debug(self, enabled: bool) -> Self;
    pub fn refresh_cache(self, enabled: bool) -> Self;
    pub async fn execute(self) -> Result<WorkflowRunResult, WorkflowRunError>;
}

pub mod sihub {
    use super::WorkflowRunBuilder;
    pub fn run(workflow_path: impl AsRef<Path>) -> WorkflowRunBuilder;
}

pub mod sih {
    use super::WorkflowRunBuilder;
    pub fn run(workflow_path: impl AsRef<Path>) -> WorkflowRunBuilder;
}
```

Notes:

- keep the crate workflow-first and async-first
- prefer path-oriented APIs over raw string-only APIs where ergonomic
- `catalog_path(...)` is an override path, not the primary path
- if the first implementation wraps the CLI, keep the Rust API stable and do not expose a CLI-argument-first API
