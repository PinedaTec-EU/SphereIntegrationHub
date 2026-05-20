# TypeScript SDK

Recommended package:

- `@pinedatec.eu/sphere-integration-hub-sdk`

Recommended entry point:

```ts
import { sihub, sih } from "@pinedatec.eu/sphere-integration-hub-sdk";
```

Recommended usage:

```ts
const result = await sihub
  .run("./workflows/create-account.workflow")
  .environment("local")
  .input("email", "john@doe.com")
  .varsFile("./workflows/create-account.wfvars")
  .execute();

const accountId = result.output["accountId"];
```

Suggested public API:

```ts
export type WorkflowInputs = Record<string, string>;

export interface WorkflowRunResult {
  output: Readonly<Record<string, string>>;
  workflowPath: string;
  environment: string;
  catalogVersion: string;
  catalogPath?: string;
  varsFilePath?: string;
  outputFilePath?: string;
  jsonReportPath?: string;
  htmlReportPath?: string;
  executionId?: string;
}

export interface WorkflowRunBuilder {
  environment(name: string): WorkflowRunBuilder;
  catalog(pathOrCatalog: string | ApiCatalogVersion): WorkflowRunBuilder;
  envFile(path: string): WorkflowRunBuilder;
  varsFile(path: string): WorkflowRunBuilder;
  input(key: string, value: string): WorkflowRunBuilder;
  inputs(values: WorkflowInputs): WorkflowRunBuilder;
  mocked(enabled?: boolean): WorkflowRunBuilder;
  verbose(enabled?: boolean): WorkflowRunBuilder;
  debug(enabled?: boolean): WorkflowRunBuilder;
  refreshCache(enabled?: boolean): WorkflowRunBuilder;
  execute(signal?: AbortSignal): Promise<WorkflowRunResult>;
}

export interface SihubStatic {
  run(workflowPath: string): WorkflowRunBuilder;
}

export declare const sihub: SihubStatic;
export declare const sih: SihubStatic;
```

Notes:

- `api.catalog` should be resolved automatically from the workflow path by default
- `catalog(...)` is an override, not the primary path
- `execute()` should reject with runtime validation or execution errors from the underlying engine
- if an embedded runtime is not yet available, the first implementation may shell out to `sih` internally, but the API contract should stay stable
