namespace SphereIntegrationHub.cli;

internal sealed class ConsoleOutputProvider : ICliOutputProvider
{
    public TextWriter Out => Console.Out;
    public TextWriter Error => Console.Error;
}
