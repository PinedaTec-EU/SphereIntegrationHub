namespace SphereIntegrationHub.cli;

internal enum CliRunMessageKind
{
    Info,
    Error
}

internal sealed record CliRunMessage(CliRunMessageKind Kind, string Text);

internal sealed record CliRunResult(int ExitCode, IReadOnlyList<CliRunMessage> Messages);
