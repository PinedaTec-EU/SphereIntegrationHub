namespace SphereIntegrationHub.MCP.Models;

/// <summary>
/// Detected API patterns (OAuth, CRUD, Pagination, etc.)
/// </summary>
public sealed record ApiPatternCollection
{
    public List<ApiPattern> Patterns { get; init; } = [];
}

public abstract record ApiPattern
{
    public required string Type { get; init; }
    public required double Confidence { get; init; }
}

public sealed record OAuth2Pattern : ApiPattern
{
    public required Dictionary<string, string> Endpoints { get; init; }
    public List<string> GrantTypes { get; init; } = [];
    public required string TokenLocation { get; init; }
}

public sealed record CrudPattern : ApiPattern
{
    public required string Resource { get; init; }
    public required Dictionary<string, string> Endpoints { get; init; }
    public required string IdParameter { get; init; }
    public required string IdType { get; init; }
}

public sealed record PaginationPattern : ApiPattern
{
    public required string Mechanism { get; init; } // "offset-limit", "cursor", "page-number"
    public required Dictionary<string, string> QueryParams { get; init; }
    public Dictionary<string, object>? Default { get; init; }
    public required PaginationResponseSchema ResponseSchema { get; init; }
}

public sealed record PaginationResponseSchema
{
    public required string DataField { get; init; }
    public string? TotalField { get; init; }
    public string? PageField { get; init; }
    public string? HasNextField { get; init; }
}

public sealed record FilteringPattern : ApiPattern
{
    public List<string> QueryParams { get; init; } = [];
}

public sealed record BatchOperationPattern : ApiPattern
{
    public required string Endpoint { get; init; }
    public required string HttpVerb { get; init; }
    public required string ArrayField { get; init; }
}
