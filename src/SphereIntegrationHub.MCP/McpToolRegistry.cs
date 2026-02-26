using SphereIntegrationHub.MCP.Core;

namespace SphereIntegrationHub.MCP;

/// <summary>
/// Stores and resolves registered MCP tools by name.
/// </summary>
internal sealed class McpToolRegistry
{
    private readonly Dictionary<string, IMcpTool> _tools = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _tools.Count;

    public void Register(IMcpTool tool)
    {
        _tools[tool.Name] = tool;
    }

    public bool TryGet(string name, out IMcpTool? tool) => _tools.TryGetValue(name, out tool);

    public IReadOnlyCollection<IMcpTool> GetAll() => _tools.Values;
}
