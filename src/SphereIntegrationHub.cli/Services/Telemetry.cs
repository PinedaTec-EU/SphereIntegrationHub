using System.Diagnostics;
using System.Diagnostics.Metrics;

using SphereIntegrationHub.cli;

namespace SphereIntegrationHub.Services;

internal static class Telemetry
{
    internal static ActivitySource ActivitySource { get; } = new(CliConstants.ActivitySourceName);
    internal static Meter Meter { get; } = new(CliConstants.MeterName);

    // ── Swagger operations cache ─────────────────────────────────────────────
    internal static Counter<long> SwaggerCacheHits { get; } =
        Meter.CreateCounter<long>(
            "sih.cache.swagger.operations.hits",
            "{hits}",
            "Number of times a parsed Swagger document was served from the in-memory cache.");

    internal static Counter<long> SwaggerCacheMisses { get; } =
        Meter.CreateCounter<long>(
            "sih.cache.swagger.operations.misses",
            "{misses}",
            "Number of times a Swagger document had to be read from disk and parsed.");

    // ── Workflow document cache ───────────────────────────────────────────────
    internal static Counter<long> WorkflowDocumentCacheHits { get; } =
        Meter.CreateCounter<long>(
            "sih.cache.workflow.document.hits",
            "{hits}",
            "Number of times a deserialized WorkflowDocument was served from the in-memory cache.");

    internal static Counter<long> WorkflowDocumentCacheMisses { get; } =
        Meter.CreateCounter<long>(
            "sih.cache.workflow.document.misses",
            "{misses}",
            "Number of times a workflow YAML had to be read from disk and deserialized.");

    // ── Duration histograms ───────────────────────────────────────────────────
    internal static Histogram<double> SwaggerLoadDuration { get; } =
        Meter.CreateHistogram<double>(
            "sih.cache.swagger.load.duration",
            "ms",
            "Elapsed time to obtain Swagger operations (cache hit or disk read).");

    internal static Histogram<double> WorkflowLoadDuration { get; } =
        Meter.CreateHistogram<double>(
            "sih.cache.workflow.load.duration",
            "ms",
            "Elapsed time to obtain a WorkflowDocument (cache hit or disk deserialize).");
}
