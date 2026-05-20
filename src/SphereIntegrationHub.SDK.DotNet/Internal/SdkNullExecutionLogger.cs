using SphereIntegrationHub.Services.Interfaces;

namespace SphereIntegrationHub.Sdk.Internal;

internal sealed class SdkNullExecutionLogger : IExecutionLogger
{
    public void Info(string message)
    {
    }

    public void Error(string message)
    {
    }
}
