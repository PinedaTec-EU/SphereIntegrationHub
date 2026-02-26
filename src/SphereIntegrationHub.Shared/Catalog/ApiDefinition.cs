namespace SphereIntegrationHub.Definitions;

public sealed class ApiDefinition
{
    public required string Name { get; set; }
    public required string SwaggerUrl { get; set; }
    public Dictionary<string, string>? BaseUrl { get; set; }
    public int? Port { get; set; }
    public string? BasePath { get; set; }
}
