namespace SphereIntegrationHub.cli;

internal sealed record InlineArguments(
    string? WorkflowPath = null,
    string? Environment = null,
    string? CatalogPath = null,
    string? EnvFileOverride = null,
    string? VarsFilePath = null,
    bool RefreshCache = false,
    bool DryRun = false,
    bool Verbose = false,
    bool Debug = false,
    bool Mocked = false,
    Dictionary<string, string>? Inputs = null,
    bool ShowHelp = false,
    string? Error = null)
{
    public Dictionary<string, string> Inputs { get; init; } =
        Inputs ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
