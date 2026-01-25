namespace SphereIntegrationHub.Services.Interfaces;

public interface IExecutionLogger
{
    void Info(string message);
    void Error(string message);
}
