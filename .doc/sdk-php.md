# PHP SDK

Recommended package:

- `pinedatec/sphere-integration-hub`

Recommended entry point:

```php
use PinedaTec\SphereIntegrationHub\sihub;
use PinedaTec\SphereIntegrationHub\sih;
```

Recommended usage:

```php
$result = sihub::run('./workflows/create-account.workflow')
    ->environment('local')
    ->input('email', 'john@doe.com')
    ->varsFile('./workflows/create-account.wfvars')
    ->execute();

$accountId = $result->output()['accountId'];
```

Suggested public API:

```php
<?php

namespace PinedaTec\SphereIntegrationHub;

final class WorkflowRunResult
{
    public function output(): array {}
    public function workflowPath(): string {}
    public function environment(): string {}
    public function catalogVersion(): string {}
    public function catalogPath(): ?string {}
    public function varsFilePath(): ?string {}
    public function outputFilePath(): ?string {}
    public function jsonReportPath(): ?string {}
    public function htmlReportPath(): ?string {}
    public function executionId(): ?string {}
}

final class WorkflowRunBuilder
{
    public function environment(string $environment): self {}
    public function catalog(string|ApiCatalogVersion $catalog): self {}
    public function envFile(string $path): self {}
    public function varsFile(string $path): self {}
    public function input(string $key, string $value): self {}
    public function inputs(array $inputs): self {}
    public function mocked(bool $enabled = true): self {}
    public function verbose(bool $enabled = true): self {}
    public function debug(bool $enabled = true): self {}
    public function refreshCache(bool $enabled = true): self {}
    public function execute(): WorkflowRunResult {}
}

final class sihub
{
    public static function run(string $workflowPath): WorkflowRunBuilder {}
}

final class sih
{
    public static function run(string $workflowPath): WorkflowRunBuilder {}
}
```

Notes:

- keep PHP naming native where it helps, but preserve the same execution semantics as the other SDKs
- `catalog(...)` remains an override path
- the package should be orchestration-host only, not a code DSL for workflow definition
- first implementation may wrap `sih`, but the public API should remain object-oriented and workflow-first
