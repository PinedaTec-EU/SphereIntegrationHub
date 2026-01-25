namespace SphereIntegrationHub.cli;

internal interface ICliArgumentParser
{
    InlineArguments ParseArgs(string[] args);
}
