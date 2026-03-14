namespace SphereIntegrationHub.MCP.Services.Integration;

/// <summary>
/// Configurable paths for MCP runtime integration with SphereIntegrationHub resources.
/// Any relative path is resolved against <see cref="ProjectRoot"/>.
/// </summary>
public sealed class SihPathOptions
{
    public string? ProjectRoot { get; init; }
    public string? ResourcesPath { get; init; }
    public string? ApiCatalogPath { get; init; }
    public string? CachePath { get; init; }
    public string? WorkflowsPath { get; init; }
}
