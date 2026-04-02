using SphereIntegrationHub.cli;
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

    [Fact]
    public void AnsiColorTextWriter_WhenEnabled_WrapsContentWithAnsiCodes()
    {
        using var writer = new StringWriter();
        using var colorWriter = new AnsiColorTextWriter(writer, "\u001b[32m", enabled: true);

        colorWriter.WriteLine("ok");

        Assert.Equal("\u001b[32mok\u001b[0m" + Environment.NewLine, writer.ToString());
    }

    [Fact]
    public void AnsiColorTextWriter_WhenDisabled_WritesPlainText()
    {
        using var writer = new StringWriter();
        using var colorWriter = new AnsiColorTextWriter(writer, "\u001b[31m", enabled: false);

        colorWriter.WriteLine("error");

        Assert.Equal($"error{Environment.NewLine}", writer.ToString());
    }
}
