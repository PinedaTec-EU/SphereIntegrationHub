using SphereIntegrationHub.MCP;
using SphereIntegrationHub.MCP.Services.Catalog;
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
    WriteIndented = false,
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
if (!servicesAdapter.ApiCatalogExists)
{
    Console.Error.WriteLine(
        "[SphereIntegrationHub.MCP] Warning: API catalog file does not exist yet. " +
        "You can create it with the generate_api_catalog_file tool.");
}
else
{
    await WarnIfCatalogContainsHtmlSwaggerUrlsAsync(servicesAdapter.ApiCatalogPath);
}

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

static async Task WarnIfCatalogContainsHtmlSwaggerUrlsAsync(string catalogPath)
{
    try
    {
        var json = await File.ReadAllTextAsync(catalogPath);
        var catalog = JsonSerializer.Deserialize<List<ApiCatalogVersion>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];

        var htmlEntries = catalog
            .SelectMany(v => v.Definitions.Select(d => new { v.Version, d.Name, d.SwaggerUrl }))
            .Where(x => LooksLikeHtmlSwaggerUrl(x.SwaggerUrl))
            .ToList();

        if (htmlEntries.Count == 0)
        {
            return;
        }

        Console.Error.WriteLine(
            $"[SphereIntegrationHub.MCP] Warning: Found {htmlEntries.Count} catalog definition(s) using HTML swaggerUrl. " +
            "MCP will try JSON fallback patterns, but this should be corrected to JSON spec URLs.");

        foreach (var entry in htmlEntries)
        {
            Console.Error.WriteLine(
                $"[SphereIntegrationHub.MCP] Warning: version={entry.Version}, api={entry.Name}, swaggerUrl={entry.SwaggerUrl}");
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(
            $"[SphereIntegrationHub.MCP] Warning: Could not inspect catalog for HTML swaggerUrl entries: {ex.Message}");
    }
}

static bool LooksLikeHtmlSwaggerUrl(string? swaggerUrl)
{
    if (string.IsNullOrWhiteSpace(swaggerUrl))
    {
        return false;
    }

    string path;
    if (Uri.TryCreate(swaggerUrl, UriKind.Absolute, out var absolute))
    {
        path = absolute.AbsolutePath;
    }
    else
    {
        path = swaggerUrl.Split('?', '#')[0];
    }

    return path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
           path.EndsWith("/swagger", StringComparison.OrdinalIgnoreCase);
}
