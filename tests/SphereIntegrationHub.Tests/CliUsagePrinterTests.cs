using SphereIntegrationHub.cli;

namespace SphereIntegrationHub.Tests;

public sealed class CliUsagePrinterTests
{
    [Fact]
    public void PrintUsage_WritesUsageBanner()
    {
        using var writer = new StringWriter();
        ICliUsagePrinter printer = new CliUsagePrinter();

        printer.PrintUsage(writer);

        var output = writer.ToString();
        Assert.Contains("Usage:", output, StringComparison.Ordinal);
        Assert.Contains("--workflow", output, StringComparison.Ordinal);
        Assert.Contains("--env", output, StringComparison.Ordinal);
    }
}
