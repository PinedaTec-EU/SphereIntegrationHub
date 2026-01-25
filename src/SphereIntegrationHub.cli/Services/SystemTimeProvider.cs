using SphereIntegrationHub.Services.Interfaces;

namespace SphereIntegrationHub.Services;

public sealed class SystemTimeProvider : ISystemTimeProvider
{
    public DateTimeOffset Now => DateTimeOffset.Now;
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
