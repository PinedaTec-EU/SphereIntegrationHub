using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services.Interfaces;

namespace SphereIntegrationHub.Services;

public sealed class ApiSwaggerCacheService
{
    private readonly HttpClient _httpClient;
    private readonly IExecutionLogger _logger;

    public ApiSwaggerCacheService(HttpClient httpClient, IExecutionLogger? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? new ConsoleExecutionLogger();
    }

    public async Task CacheSwaggerAsync(
        ApiCatalogVersion catalogVersion,
        string environment,
        string cacheRoot,
        bool refresh,
        bool verbose,
        CancellationToken cancellationToken)
    {
        if (catalogVersion.Definitions.Count == 0)
        {
            return;
        }

        Directory.CreateDirectory(cacheRoot);

        foreach (var definition in catalogVersion.Definitions.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            using var activity = Telemetry.ActivitySource.StartActivity(TelemetryConstants.ActivitySwaggerCache);

            activity?.SetTag(TelemetryConstants.TagApiDefinition, definition.Name);
            activity?.SetTag(TelemetryConstants.TagCatalogVersion, catalogVersion.Version);

            if (!ApiBaseUrlResolver.TryResolveBaseUrl(catalogVersion, definition, environment, out var baseUrl))
            {
                throw new InvalidOperationException(
                    $"Environment '{environment}' was not found for API definition '{definition.Name}' in catalog version '{catalogVersion.Version}'.");
            }

            var swaggerUri = CatalogUrlResolver.ResolveSwaggerUri(catalogVersion, definition, environment);
            var cachePath = Path.Combine(cacheRoot, $"{definition.Name}.json");
            if (!refresh && File.Exists(cachePath))
            {
                if (verbose)
                {
                    _logger.Info($"Swagger cache hit for '{definition.Name}' (version {catalogVersion.Version}): {ToRelativePath(cachePath)}");
                }
                continue;
            }

            if (verbose)
            {
                _logger.Info($"Swagger cache miss for '{definition.Name}' (version {catalogVersion.Version}): {ToRelativePath(cachePath)}");
            }

            if (swaggerUri.IsFile)
            {
                var localPath = swaggerUri.LocalPath;
                if (!File.Exists(localPath))
                {
                    if (verbose)
                    {
                        _logger.Info($"Swagger source file not found: {ToRelativePath(localPath)}");
                        _logger.Info($"Swagger source uri: {swaggerUri}");
                        _logger.Info($"Swagger cache expected at: {ToRelativePath(cachePath)}");
                    }

                    throw new FileNotFoundException(
                        $"Swagger source file was not found at '{localPath}'.",
                        localPath);
                }

                var payload = await File.ReadAllTextAsync(localPath, cancellationToken);
                await File.WriteAllTextAsync(cachePath, payload, cancellationToken);
                if (verbose)
                {
                    _logger.Info($"Swagger cached for '{definition.Name}' (version {catalogVersion.Version}) from file: {ToRelativePath(cachePath)}");
                }
            }
            else
            {
                try
                {
                    var payload = await _httpClient.GetStringAsync(swaggerUri, cancellationToken);
                    await File.WriteAllTextAsync(cachePath, payload, cancellationToken);
                    if (verbose)
                    {
                        _logger.Info($"Swagger cached for '{definition.Name}' (version {catalogVersion.Version}) from url: {ToRelativePath(cachePath)}");
                    }
                }
                catch (Exception ex)
                {
                    if (verbose)
                    {
                        _logger.Info($"Swagger download failed: {swaggerUri}");
                    }

                    throw new InvalidOperationException($"Failed to download swagger for '{definition.Name}' from '{swaggerUri}': {ex.Message}", ex);
                }
            }
        }
    }

    private static string ToRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var baseDirectory = AppContext.BaseDirectory;
        if (Path.IsPathRooted(path) &&
            !string.IsNullOrWhiteSpace(baseDirectory) &&
            path.StartsWith(baseDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return path[baseDirectory.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        return path;
    }
}
