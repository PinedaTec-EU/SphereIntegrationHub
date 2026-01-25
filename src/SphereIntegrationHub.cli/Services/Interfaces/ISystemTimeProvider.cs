namespace SphereIntegrationHub.Services.Interfaces;

public interface ISystemTimeProvider
{
    DateTimeOffset Now { get; }
    DateTimeOffset UtcNow { get; }
}
