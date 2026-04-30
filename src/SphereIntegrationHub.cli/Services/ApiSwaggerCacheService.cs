using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services.Interfaces;
using System.Diagnostics;
using System.Net;

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

    public async Task<IReadOnlyList<WorkflowPreflightOperationRecord>> CacheSwaggerAsync(
        ApiCatalogVersion catalogVersion,
        string environment,
        string cacheRoot,
        bool refresh,
        bool verbose,
        CancellationToken cancellationToken)
    {
        if (catalogVersion.Definitions.Count == 0)
        {
            return [];
        }

        Directory.CreateDirectory(cacheRoot);
        var operations = new List<WorkflowPreflightOperationRecord>();

        foreach (var definition in catalogVersion.Definitions.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (string.Equals(definition.GetResolvedContractType(), ApiContractTypes.Llm, StringComparison.OrdinalIgnoreCase))
            {
                if (verbose)
                {
                    _logger.Info($"Skipping swagger cache for LLM definition '{definition.Name}'.");
                }

                continue;
            }

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
                operations.Add(new WorkflowPreflightOperationRecord
                {
                    OperationType = "SwaggerCache",
                    DefinitionName = definition.Name,
                    Target = swaggerUri.ToString(),
                    Status = "Ok",
                    Message = $"Cache hit: {ToRelativePath(cachePath)}",
                    DurationMs = 0
                });
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
                operations.Add(new WorkflowPreflightOperationRecord
                {
                    OperationType = "SwaggerCache",
                    DefinitionName = definition.Name,
                    Target = swaggerUri.ToString(),
                    Status = "Ok",
                    Message = $"Cached from file: {ToRelativePath(cachePath)}",
                    DurationMs = 0
                });
                if (verbose)
                {
                    _logger.Info($"Swagger cached for '{definition.Name}' (version {catalogVersion.Version}) from file: {ToRelativePath(cachePath)}");
                }
            }
            else
            {
                var operation = await DownloadSwaggerWithRetryAsync(definition, swaggerUri, cachePath, verbose, cancellationToken);
                operations.Add(operation);
            }
        }

        return operations;
    }

    private async Task<WorkflowPreflightOperationRecord> DownloadSwaggerWithRetryAsync(
        ApiDefinition definition,
        Uri swaggerUri,
        string cachePath,
        bool verbose,
        CancellationToken cancellationToken)
    {
        var policy = ApiReadinessPolicyResolver.Resolve(definition);
        var operation = new WorkflowPreflightOperationRecord
        {
            OperationType = "SwaggerCache",
            DefinitionName = definition.Name,
            Target = swaggerUri.ToString()
        };
        var operationTimer = Stopwatch.StartNew();

        for (var attemptNumber = 1; attemptNumber <= policy.MaxRetries + 1; attemptNumber++)
        {
            var attemptTimer = Stopwatch.StartNew();
            var attempt = new WorkflowPreflightAttemptRecord
            {
                AttemptNumber = attemptNumber,
                RequestUri = swaggerUri.ToString(),
                StartedAtUtc = DateTimeOffset.UtcNow
            };

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, swaggerUri);
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(policy.TimeoutMs);
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
                var payload = await response.Content.ReadAsStringAsync(timeoutCts.Token);

                if (response.IsSuccessStatusCode)
                {
                    await File.WriteAllTextAsync(cachePath, payload, cancellationToken);
                    attempt.Status = "Ok";
                    attempt.HttpStatusCode = (int)response.StatusCode;
                    attemptTimer.Stop();
                    attempt.FinishedAtUtc = attempt.StartedAtUtc.AddMilliseconds(attemptTimer.ElapsedMilliseconds);
                    attempt.DurationMs = attemptTimer.ElapsedMilliseconds;
                    operation.Attempts.Add(attempt);
                    operationTimer.Stop();
                    operation.Status = "Ok";
                    operation.Message = $"Cached from url: {ToRelativePath(cachePath)}";
                    operation.RetryCount = operation.Attempts.Count - 1;
                    operation.DurationMs = operationTimer.ElapsedMilliseconds;
                    if (verbose)
                    {
                        _logger.Info($"Swagger cached for '{definition.Name}' from url: {ToRelativePath(cachePath)}");
                    }

                    return operation;
                }

                attempt.Status = "Error";
                attempt.HttpStatusCode = (int)response.StatusCode;
                attempt.ErrorMessage = $"Received HTTP {(int)response.StatusCode} ({response.StatusCode}).";
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                attempt.Status = "Error";
                attempt.ErrorMessage = $"Swagger download timed out after {TimeSpan.FromMilliseconds(policy.TimeoutMs).TotalSeconds:F0} s.";
            }
            catch (Exception ex)
            {
                attempt.Status = "Error";
                attempt.ErrorMessage = ex.Message;
            }

            attemptTimer.Stop();
            attempt.FinishedAtUtc = attempt.StartedAtUtc.AddMilliseconds(attemptTimer.ElapsedMilliseconds);
            attempt.DurationMs = attemptTimer.ElapsedMilliseconds;
            operation.Attempts.Add(attempt);

            if (attemptNumber <= policy.MaxRetries && policy.DelayMs > 0)
            {
                await Task.Delay(policy.DelayMs, cancellationToken);
            }
        }

        operationTimer.Stop();
        operation.Status = "Error";
        operation.Message = operation.Attempts[^1].ErrorMessage;
        operation.RetryCount = operation.Attempts.Count - 1;
        operation.DurationMs = operationTimer.ElapsedMilliseconds;

        if (verbose)
        {
            _logger.Info($"Swagger download failed: {swaggerUri}");
        }

        var attemptSuffix = operation.Attempts.Count > 1
            ? $" after {operation.Attempts.Count} attempt(s)"
            : "";
        throw new InvalidOperationException($"Failed to download swagger for '{definition.Name}' from '{swaggerUri}'{attemptSuffix}: {operation.Message}");
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
