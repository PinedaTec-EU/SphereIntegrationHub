using System.Text.Json;

using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.Services;

public sealed class ApiCatalogReader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public IReadOnlyList<ApiCatalogVersion> Load(string catalogPath)
    {
        using var activity = Telemetry.ActivitySource.StartActivity(TelemetryConstants.ActivityCatalogLoad);
        activity?.SetTag(TelemetryConstants.TagCatalogPath, catalogPath);
        if (string.IsNullOrWhiteSpace(catalogPath))
        {
            throw new ArgumentException("Catalog path is required.", nameof(catalogPath));
        }

        if (!File.Exists(catalogPath))
        {
            throw new FileNotFoundException("Catalog file was not found.", catalogPath);
        }

        var json = File.ReadAllText(catalogPath);
        var catalog = JsonSerializer.Deserialize<List<ApiCatalogVersion>>(json, Options);
        if (catalog is null || catalog.Count == 0)
        {
            throw new InvalidOperationException("Catalog file is empty or invalid.");
        }

        return catalog;
    }
}
