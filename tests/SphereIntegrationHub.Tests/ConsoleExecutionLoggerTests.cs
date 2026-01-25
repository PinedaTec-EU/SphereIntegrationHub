using SphereIntegrationHub.Services;

namespace SphereIntegrationHub.Tests;

public sealed class ConsoleExecutionLoggerTests
{
    [Fact]
    public void WritesMessagesToConfiguredWriters()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var logger = new ConsoleExecutionLogger(output, error);

        logger.Info("info");
        logger.Error("error");

        Assert.Equal($"info{Environment.NewLine}", output.ToString());
        Assert.Equal($"error{Environment.NewLine}", error.ToString());
    }
}
