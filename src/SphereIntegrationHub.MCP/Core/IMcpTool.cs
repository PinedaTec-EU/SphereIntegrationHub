namespace SphereIntegrationHub.MCP.Core;

/// <summary>
/// Interface for all MCP tools
/// </summary>
public interface IMcpTool
{
    /// <summary>
    /// The unique name of the tool (must match MCP protocol requirements)
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Description of what the tool does
    /// </summary>
    string Description { get; }

    /// <summary>
    /// JSON schema for the tool's input parameters
    /// </summary>
    object InputSchema { get; }

    /// <summary>
    /// Executes the tool with the given arguments
    /// </summary>
    /// <param name="arguments">Tool arguments as JSON object</param>
    /// <returns>Tool result as object (will be serialized to JSON)</returns>
    Task<object> ExecuteAsync(Dictionary<string, object>? arguments);
}
