using System;

namespace SphereIntegrationHub.Services;

public sealed class CircuitBreakerState
{
    public int ConsecutiveFailures { get; set; }
    public int ConsecutiveSuccesses { get; set; }
    public DateTimeOffset? OpenUntil { get; set; }
    public bool HalfOpen { get; set; }
}
