using SphereIntegrationHub.MCP;
using SphereIntegrationHub.MCP.Services.Integration;
using System.Text.Json;

const string ProjectRootEnv = "SIH_PROJECT_ROOT";
const string ResourcesPathEnv = "SIH_RESOURCES_PATH";
const string ApiCatalogPathEnv = "SIH_API_CATALOG_PATH";
const string CachePathEnv = "SIH_CACHE_PATH";
const string WorkflowsPathEnv = "SIH_WORKFLOWS_PATH";

// Configure JSON serializer options
var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
};

var pathOptions = new SihPathOptions
{
    ProjectRoot = Environment.GetEnvironmentVariable(ProjectRootEnv) ?? Directory.GetCurrentDirectory(),
    ResourcesPath = Environment.GetEnvironmentVariable(ResourcesPathEnv),
    ApiCatalogPath = Environment.GetEnvironmentVariable(ApiCatalogPathEnv),
    CachePath = Environment.GetEnvironmentVariable(CachePathEnv),
    WorkflowsPath = Environment.GetEnvironmentVariable(WorkflowsPathEnv)
};

// Initialize services adapter (bridge to main CLI services)
var servicesAdapter = new SihServicesAdapter(pathOptions);

// Create and start MCP server
var mcpServer = new McpServer(servicesAdapter, jsonOptions);

Console.Error.WriteLine($"[SphereIntegrationHub.MCP] Starting server...");
Console.Error.WriteLine($"[SphereIntegrationHub.MCP] Project root: {servicesAdapter.ProjectRoot}");
Console.Error.WriteLine($"[SphereIntegrationHub.MCP] API catalog: {servicesAdapter.ApiCatalogPath}");
Console.Error.WriteLine($"[SphereIntegrationHub.MCP] Cache path: {servicesAdapter.CachePath}");
Console.Error.WriteLine($"[SphereIntegrationHub.MCP] Workflows path: {servicesAdapter.WorkflowsPath}");

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
