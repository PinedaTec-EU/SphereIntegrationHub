namespace SphereIntegrationHub.cli;

internal interface ICliOutputProvider
{
    TextWriter Out { get; }
    TextWriter Error { get; }
}
