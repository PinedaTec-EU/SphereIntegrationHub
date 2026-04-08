namespace SphereIntegrationHub.Definitions;

public sealed class ApiDefinition
{
    public required string Name { get; set; }
    public required string SwaggerUrl { get; set; }
    public string? HealthCheck { get; set; }
    public ApiReadinessPolicyDefinition? Readiness { get; set; }
    public Dictionary<string, string>? BaseUrl { get; set; }
    public int? Port { get; set; }
    public string? BasePath { get; set; }
}

public sealed class ApiReadinessPolicyDefinition
{
    public int? MaxRetries { get; set; }
    public int? DelayMs { get; set; }
    public int? TimeoutMs { get; set; }
    public int[]? HttpStatus { get; set; }
}
