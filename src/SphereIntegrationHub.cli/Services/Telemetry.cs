using System.Diagnostics;
using System.Diagnostics.Metrics;

using SphereIntegrationHub.cli;

namespace SphereIntegrationHub.Services;

internal static class Telemetry
{
    internal static ActivitySource ActivitySource { get; } = new(CliConstants.ActivitySourceName);
    internal static Meter Meter { get; } = new(CliConstants.MeterName);
}
