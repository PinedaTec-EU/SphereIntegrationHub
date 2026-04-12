using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.Services;

public sealed class ApiCatalogReader
{
    public IReadOnlyList<ApiCatalogVersion> Load(string catalogPath)
    {
        using var activity = Telemetry.ActivitySource.StartActivity(TelemetryConstants.ActivityCatalogLoad);
        activity?.SetTag(TelemetryConstants.TagCatalogPath, catalogPath);
        if (string.IsNullOrWhiteSpace(catalogPath))
        {
            throw new ArgumentException("Catalog path is required.", nameof(catalogPath));
        }

        return ApiCatalogFile.Load(catalogPath);
    }
}
