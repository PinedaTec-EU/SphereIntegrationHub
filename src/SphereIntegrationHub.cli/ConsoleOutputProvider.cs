namespace SphereIntegrationHub.cli;

internal sealed class ConsoleOutputProvider : ICliOutputProvider
{
    private const string GreenAnsiCode = "\u001b[32m";
    private const string RedAnsiCode = "\u001b[31m";
    private const string NoColorEnvironmentVariable = "NO_COLOR";

    public ConsoleOutputProvider()
    {
        var useOutputColors = IsColorEnabled(Console.IsOutputRedirected);
        var useErrorColors = IsColorEnabled(Console.IsErrorRedirected);

        Out = new AnsiColorTextWriter(Console.Out, GreenAnsiCode, useOutputColors);
        Error = new AnsiColorTextWriter(Console.Error, RedAnsiCode, useErrorColors);
    }

    public TextWriter Out { get; }
    public TextWriter Error { get; }

    private static bool IsColorEnabled(bool isRedirected)
        => !isRedirected && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(NoColorEnvironmentVariable));
}
