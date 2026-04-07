using System.Diagnostics.Metrics;

namespace SphereIntegrationHub.MCP;

/// <summary>
/// Central telemetry instruments for the MCP server.
/// </summary>
internal static class McpTelemetry
{
    private const string MeterName = "SphereIntegrationHub.MCP";

    internal static Meter Meter { get; } = new(MeterName);

    // ── Validation result cache ───────────────────────────────────────────────
    internal static Counter<long> ValidationCacheHits { get; } =
        Meter.CreateCounter<long>(
            "sih.cache.workflow.validation.hits",
            "{hits}",
            "Number of times a ValidationResult was served from the content-hash cache.");

    internal static Counter<long> ValidationCacheMisses { get; } =
        Meter.CreateCounter<long>(
            "sih.cache.workflow.validation.misses",
            "{misses}",
            "Number of times validation had to be computed because the content was not cached.");

    internal static Counter<long> ValidationCacheEvictions { get; } =
        Meter.CreateCounter<long>(
            "sih.cache.workflow.validation.evictions",
            "{evictions}",
            "Number of entries removed from the validation cache to enforce the size limit.");

    internal static UpDownCounter<int> ValidationCacheSize { get; } =
        Meter.CreateUpDownCounter<int>(
            "sih.cache.workflow.validation.size",
            "{entries}",
            "Current number of entries in the validation result cache.");

    // ── Duration histograms ───────────────────────────────────────────────────
    internal static Histogram<double> ValidationDuration { get; } =
        Meter.CreateHistogram<double>(
            "sih.cache.workflow.validation.duration",
            "ms",
            "Elapsed time to obtain a ValidationResult (cache hit or full validation).");
}
