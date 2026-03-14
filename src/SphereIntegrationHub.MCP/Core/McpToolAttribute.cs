namespace SphereIntegrationHub.MCP.Core;

/// <summary>
/// Attribute to mark MCP tool classes with metadata
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class McpToolAttribute : Attribute
{
    public McpToolAttribute(string name, string description)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description ?? throw new ArgumentNullException(nameof(description));
    }

    public string Name { get; }
    public string Description { get; }
    public string? Category { get; set; }
    public string? Level { get; set; }
}
