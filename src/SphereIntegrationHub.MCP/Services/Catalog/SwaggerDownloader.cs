using System.Text.Json;

namespace SphereIntegrationHub.MCP.Services.Catalog;

internal static class SwaggerDownloader
{
    public static async Task<string> DownloadAsync(Uri swaggerUri)
    {
        string payload;
        if (swaggerUri.IsFile)
        {
            if (!File.Exists(swaggerUri.LocalPath))
            {
                throw new FileNotFoundException(
                    $"Swagger source file not found: {swaggerUri.LocalPath}", swaggerUri.LocalPath);
            }

            payload = await File.ReadAllTextAsync(swaggerUri.LocalPath);
            if (IsLikelyHtml(payload))
            {
                Console.Error.WriteLine(
                    $"[SphereIntegrationHub.MCP] Warning: swagger source '{swaggerUri}' returned HTML. Trying known JSON fallback URLs.");
                var fallback = await TryResolveOpenApiFromHtmlFallbackAsync(swaggerUri);
                if (fallback != null)
                    return fallback;

                throw new InvalidOperationException(
                    $"Swagger source '{swaggerUri}' returned HTML content. Tried JSON fallbacks but none returned a valid OpenAPI document.");
            }

            ValidatePayload(payload, swaggerUri);
            return payload;
        }

        using var httpClient = CreateHttpClient();
        payload = await httpClient.GetStringAsync(swaggerUri);
        if (IsLikelyHtml(payload))
        {
            Console.Error.WriteLine(
                $"[SphereIntegrationHub.MCP] Warning: swagger source '{swaggerUri}' returned HTML. Trying known JSON fallback URLs.");
            var fallback = await TryResolveOpenApiFromHtmlFallbackAsync(swaggerUri, httpClient);
            if (fallback != null)
                return fallback;

            throw new InvalidOperationException(
                $"Swagger source '{swaggerUri}' returned HTML content. Tried JSON fallbacks but none returned a valid OpenAPI document.");
        }

        ValidatePayload(payload, swaggerUri);
        return payload;
    }

    internal static bool IsLikelyHtml(string payload)
    {
        var trimmed = payload.TrimStart();
        return trimmed.StartsWith("<", StringComparison.Ordinal);
    }

    private static void ValidatePayload(string payload, Uri sourceUri)
    {
        var trimmed = payload.TrimStart();
        if (trimmed.StartsWith("<", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Swagger source '{sourceUri}' returned HTML content. Use the OpenAPI JSON endpoint (for example: /swagger/v1/swagger.json or /openapi.json).");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Swagger source '{sourceUri}' did not return valid JSON: {ex.Message}", ex);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    $"Swagger source '{sourceUri}' returned JSON but not an OpenAPI document object.");
            }

            var root = document.RootElement;
            if (!root.TryGetProperty("openapi", out _) && !root.TryGetProperty("swagger", out _))
            {
                throw new InvalidOperationException(
                    $"Swagger source '{sourceUri}' returned JSON but missing 'openapi'/'swagger' fields.");
            }
        }
    }

    private static async Task<string?> TryResolveOpenApiFromHtmlFallbackAsync(
        Uri sourceUri, HttpClient? sharedClient = null)
    {
        var candidates = BuildFallbackCandidates(sourceUri);
        if (candidates.Count == 0)
            return null;

        foreach (var candidate in candidates)
        {
            try
            {
                string candidatePayload;
                if (candidate.IsFile)
                {
                    if (!File.Exists(candidate.LocalPath))
                        continue;
                    candidatePayload = await File.ReadAllTextAsync(candidate.LocalPath);
                }
                else if (sharedClient != null)
                {
                    candidatePayload = await sharedClient.GetStringAsync(candidate);
                }
                else
                {
                    using var client = CreateHttpClient();
                    candidatePayload = await client.GetStringAsync(candidate);
                }

                if (IsLikelyHtml(candidatePayload))
                    continue;

                ValidatePayload(candidatePayload, candidate);
                Console.Error.WriteLine(
                    $"[SphereIntegrationHub.MCP] Info: resolved OpenAPI fallback URL '{candidate}' from HTML source '{sourceUri}'.");
                return candidatePayload;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[SphereIntegrationHub.MCP] Warning: fallback candidate '{candidate}' failed — {ex.Message}");
            }
        }

        return null;
    }

    private static List<Uri> BuildFallbackCandidates(Uri sourceUri)
    {
        var path = sourceUri.AbsolutePath;
        if (string.IsNullOrWhiteSpace(path))
            return [];

        var prefix = path;
        if (prefix.EndsWith("/index.html", StringComparison.OrdinalIgnoreCase))
            prefix = prefix[..^"/index.html".Length];
        else if (prefix.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            var slashIndex = prefix.LastIndexOf('/');
            prefix = slashIndex > 0 ? prefix[..slashIndex] : prefix;
        }

        prefix = prefix.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(prefix))
            return [];

        var candidatePaths = new List<string>
        {
            $"{prefix}/v1/swagger.json",
            $"{prefix}/swagger.json",
            $"{prefix}/openapi.json"
        };

        if (prefix.EndsWith("/swagger", StringComparison.OrdinalIgnoreCase))
        {
            var parent = prefix[..^"/swagger".Length];
            if (!string.IsNullOrWhiteSpace(parent))
            {
                candidatePaths.Add($"{parent}/swagger/v1/swagger.json");
                candidatePaths.Add($"{parent}/swagger.json");
                candidatePaths.Add($"{parent}/openapi.json");
            }
        }

        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<Uri>();
        foreach (var candidatePath in candidatePaths)
        {
            var builder = new UriBuilder(sourceUri)
            {
                Path = candidatePath,
                Query = string.Empty,
                Fragment = string.Empty
            };
            var uri = builder.Uri;
            if (!uri.AbsoluteUri.Equals(sourceUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase) &&
                unique.Add(uri.AbsoluteUri))
            {
                candidates.Add(uri);
            }
        }

        return candidates;
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler { UseCookies = false };
        return new HttpClient(handler, disposeHandler: true);
    }
}
