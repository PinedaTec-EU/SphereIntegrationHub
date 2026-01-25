namespace SphereIntegrationHub.cli;

internal interface ICliPipeline
{
    Task<CliRunResult> RunAsync(InlineArguments parseResult, CancellationToken cancellationToken);
}
