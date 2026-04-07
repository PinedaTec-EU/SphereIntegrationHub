using Xunit;

// Disable parallel test execution across the entire assembly.
// ValidationCacheMetricsTests observes a shared static Meter (McpTelemetry) and asserts
// exact measurement counts. Running test classes in parallel would cause other tests that
// instantiate WorkflowValidatorService (via ValidateWorkflowTool, RepairWorkflowArtifactsTool,
// etc.) to emit measurements into active MetricsCollector listeners, making those assertions
// non-deterministic.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
