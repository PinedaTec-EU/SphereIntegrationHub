namespace SphereIntegrationHub.MCP.Models;

/// <summary>
/// Information about an API endpoint extracted from Swagger
/// </summary>
public sealed record EndpointInfo
{
    public required string ApiName { get; init; }
    public required string Endpoint { get; init; }
    public required string HttpVerb { get; init; }
    public required string Summary { get; init; }
    public required string Description { get; init; }
    public List<ParameterInfo> QueryParameters { get; init; } = [];
    public List<ParameterInfo> HeaderParameters { get; init; } = [];
    public List<ParameterInfo> PathParameters { get; init; } = [];
    public BodySchema? BodySchema { get; init; }
    public Dictionary<int, ResponseSchema> Responses { get; init; } = [];
    public List<string> Tags { get; init; } = [];
}

public sealed record ParameterInfo
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required bool Required { get; init; }
    public string? Description { get; init; }
    public string? DefaultValue { get; init; }
}

public sealed record BodySchema
{
    public required Dictionary<string, FieldSchema> Fields { get; init; }
    public required List<string> RequiredFields { get; init; }
    public string? Example { get; init; }
}

public sealed record FieldSchema
{
    public required string Type { get; init; }
    public string? Format { get; init; }
    public string? Description { get; init; }
    public bool IsArray { get; init; }
    public List<string>? EnumValues { get; init; }
}

public sealed record ResponseSchema
{
    public required int StatusCode { get; init; }
    public required string Description { get; init; }
    public Dictionary<string, FieldSchema>? Fields { get; init; }
    public string? Example { get; init; }
}
