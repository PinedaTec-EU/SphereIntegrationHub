namespace SphereIntegrationHub.cli;

internal sealed class ConsoleOutputProvider : ICliOutputProvider
{
    private const string NoColorEnvironmentVariable = "NO_COLOR";

    public ConsoleOutputProvider()
    {
        UseColors = IsColorEnabled(Console.IsOutputRedirected) && IsColorEnabled(Console.IsErrorRedirected);
        Out = Console.Out;
        Error = Console.Error;
    }

    public TextWriter Out { get; }
    public TextWriter Error { get; }
    public bool UseColors { get; }

    private static bool IsColorEnabled(bool isRedirected)
        => !isRedirected && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(NoColorEnvironmentVariable));
}
