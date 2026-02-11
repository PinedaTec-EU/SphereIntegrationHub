namespace SphereIntegrationHub.MCP.Models;

/// <summary>
/// Report on how well workflows cover available API endpoints
/// </summary>
public sealed record CoverageReport
{
    public required string Version { get; init; }
    public required int TotalEndpoints { get; init; }
    public required int CoveredEndpoints { get; init; }
    public required int UnusedEndpoints { get; init; }
    public required double CoveragePercentage { get; init; }
    public List<EndpointUsage> EndpointUsage { get; init; } = [];
    public List<UnusedEndpointSuggestion> UnusedSuggestions { get; init; } = [];
    public Dictionary<string, ApiCoverage> ApiBreakdown { get; init; } = [];
}

public sealed record EndpointUsage
{
    public required string ApiName { get; init; }
    public required string Endpoint { get; init; }
    public required string HttpVerb { get; init; }
    public required int UsageCount { get; init; }
    public List<string> UsedInWorkflows { get; init; } = [];
}

public sealed record UnusedEndpointSuggestion
{
    public required string ApiName { get; init; }
    public required string Endpoint { get; init; }
    public required string HttpVerb { get; init; }
    public required string Summary { get; init; }
    public List<string> PossibleUseCases { get; init; } = [];
    public List<string> Tags { get; init; } = [];
}

public sealed record ApiCoverage
{
    public required string ApiName { get; init; }
    public required int TotalEndpoints { get; init; }
    public required int CoveredEndpoints { get; init; }
    public required double CoveragePercentage { get; init; }
}
