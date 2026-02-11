using SphereIntegrationHub.MCP;
using SphereIntegrationHub.MCP.Services.Integration;
using System.Text.Json;

// Configure JSON serializer options
var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
};

// Get project root from environment variable or default to current directory
var projectRoot = Environment.GetEnvironmentVariable("SIH_PROJECT_ROOT")
    ?? Directory.GetCurrentDirectory();

// Initialize services adapter (bridge to main CLI services)
var servicesAdapter = new SihServicesAdapter(projectRoot);

// Create and start MCP server
var mcpServer = new McpServer(servicesAdapter, jsonOptions);

Console.Error.WriteLine($"[SphereIntegrationHub.MCP] Starting server...");
Console.Error.WriteLine($"[SphereIntegrationHub.MCP] Project root: {projectRoot}");

try
{
    await mcpServer.StartAsync(Console.OpenStandardInput(), Console.OpenStandardOutput());
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[SphereIntegrationHub.MCP] Fatal error: {ex.Message}");
    Console.Error.WriteLine($"[SphereIntegrationHub.MCP] Stack trace: {ex.StackTrace}");
    return 1;
}

return 0;
