using Xunit;

namespace SphereIntegrationHub.Tests;

// Tests that observe the shared static Meter ("SphereIntegrationHub") and assert
// exact measurement counts must not run in parallel with other tests that emit to
// the same instruments (e.g. ApiEndpointValidatorLoggerTests → swagger cache misses).
// Placing them all in this collection guarantees sequential execution within the group
// while leaving every other test class free to parallelise normally.
[CollectionDefinition(Name)]
public sealed class CliCacheMetricsCollection
{
    public const string Name = "CliCacheMetrics";
}
