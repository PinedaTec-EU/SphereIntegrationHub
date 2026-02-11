using SphereIntegrationHub.MCP.Models;
using SphereIntegrationHub.MCP.Services.Catalog;
using SphereIntegrationHub.MCP.Services.Integration;

namespace SphereIntegrationHub.MCP.Services.Semantic;

/// <summary>
/// Detects common API patterns (OAuth, CRUD, Pagination, etc.)
/// </summary>
public sealed class PatternDetector
{
    private readonly SihServicesAdapter _adapter;
    private readonly SwaggerReader _swaggerReader;

    public PatternDetector(SihServicesAdapter adapter)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _swaggerReader = new SwaggerReader(adapter);
    }

    /// <summary>
    /// Detects all patterns in an API
    /// </summary>
    public async Task<ApiPatternCollection> DetectPatternsAsync(string version, string apiName)
    {
        var endpoints = await _swaggerReader.GetEndpointsAsync(version, apiName);
        var patterns = new List<ApiPattern>();

        // Detect OAuth pattern
        var oauthPattern = DetectOAuth(endpoints);
        if (oauthPattern != null)
        {
            patterns.Add(oauthPattern);
        }

        // Detect CRUD patterns
        patterns.AddRange(DetectCrud(endpoints));

        // Detect pagination patterns
        patterns.AddRange(DetectPagination(endpoints));

        // Detect filtering patterns
        patterns.AddRange(DetectFiltering(endpoints));

        // Detect batch operation patterns
        patterns.AddRange(DetectBatchOperations(endpoints));

        return new ApiPatternCollection
        {
            Patterns = patterns
        };
    }

    /// <summary>
    /// Detects OAuth 2.0 authentication pattern
    /// </summary>
    private static OAuth2Pattern? DetectOAuth(List<EndpointInfo> endpoints)
    {
        var tokenEndpoint = endpoints.FirstOrDefault(e =>
            e.Endpoint.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            e.Endpoint.Contains("oauth", StringComparison.OrdinalIgnoreCase));

        var authEndpoint = endpoints.FirstOrDefault(e =>
            e.Endpoint.Contains("authorize", StringComparison.OrdinalIgnoreCase) ||
            e.Endpoint.Contains("auth", StringComparison.OrdinalIgnoreCase));

        if (tokenEndpoint == null)
        {
            return null;
        }

        var oauthEndpoints = new Dictionary<string, string>();
        var grantTypes = new List<string>();

        if (tokenEndpoint != null)
        {
            oauthEndpoints["token"] = $"{tokenEndpoint.HttpVerb} {tokenEndpoint.Endpoint}";

            // Check for grant_type parameter
            if (tokenEndpoint.BodySchema?.Fields.ContainsKey("grant_type") == true)
            {
                grantTypes.Add("client_credentials");
            }
        }

        if (authEndpoint != null)
        {
            oauthEndpoints["authorize"] = $"{authEndpoint.HttpVerb} {authEndpoint.Endpoint}";
            grantTypes.Add("authorization_code");
        }

        var confidence = 0.7;
        if (grantTypes.Count > 1) confidence = 0.9;

        return new OAuth2Pattern
        {
            Type = "OAuth2",
            Confidence = confidence,
            Endpoints = oauthEndpoints,
            GrantTypes = grantTypes,
            TokenLocation = tokenEndpoint.Endpoint
        };
    }

    /// <summary>
    /// Detects CRUD (Create, Read, Update, Delete) patterns
    /// </summary>
    private static List<CrudPattern> DetectCrud(List<EndpointInfo> endpoints)
    {
        var patterns = new List<CrudPattern>();

        // Group endpoints by base path
        var groupedByPath = endpoints
            .GroupBy(e => GetBasePath(e.Endpoint))
            .Where(g => g.Count() >= 2); // At least 2 operations to be considered CRUD

        foreach (var group in groupedByPath)
        {
            var operations = new Dictionary<string, string>();
            var basePath = group.Key;

            foreach (var endpoint in group)
            {
                var operation = endpoint.HttpVerb.ToUpperInvariant() switch
                {
                    "GET" => endpoint.PathParameters.Any() ? "read" : "list",
                    "POST" => "create",
                    "PUT" => "update",
                    "DELETE" => "delete",
                    "PATCH" => "patch",
                    _ => null
                };

                if (operation != null)
                {
                    operations[operation] = $"{endpoint.HttpVerb} {endpoint.Endpoint}";
                }
            }

            // Detect ID parameter
            var idParam = group
                .SelectMany(e => e.PathParameters)
                .FirstOrDefault(p => p.Name.Contains("id", StringComparison.OrdinalIgnoreCase));

            if (operations.Count >= 2)
            {
                var confidence = operations.Count switch
                {
                    >= 4 => 0.95,
                    3 => 0.8,
                    _ => 0.6
                };

                patterns.Add(new CrudPattern
                {
                    Type = "CRUD",
                    Confidence = confidence,
                    Resource = ExtractResourceName(basePath),
                    Endpoints = operations,
                    IdParameter = idParam?.Name ?? "id",
                    IdType = idParam?.Type ?? "string"
                });
            }
        }

        return patterns;
    }

    /// <summary>
    /// Detects pagination patterns
    /// </summary>
    private static List<PaginationPattern> DetectPagination(List<EndpointInfo> endpoints)
    {
        var patterns = new List<PaginationPattern>();

        foreach (var endpoint in endpoints.Where(e => e.HttpVerb == "GET"))
        {
            var queryParams = endpoint.QueryParameters.Select(p => p.Name.ToLowerInvariant()).ToList();

            // Check for offset-limit pattern
            if (queryParams.Contains("offset") && queryParams.Contains("limit"))
            {
                patterns.Add(new PaginationPattern
                {
                    Type = "Pagination",
                    Confidence = 0.9,
                    Mechanism = "offset-limit",
                    QueryParams = new Dictionary<string, string>
                    {
                        ["offset"] = "Starting position",
                        ["limit"] = "Number of items per page"
                    },
                    ResponseSchema = DetectPaginationResponse(endpoint)
                });
            }
            // Check for page-pageSize pattern
            else if (queryParams.Contains("page") && (queryParams.Contains("pagesize") || queryParams.Contains("size")))
            {
                patterns.Add(new PaginationPattern
                {
                    Type = "Pagination",
                    Confidence = 0.9,
                    Mechanism = "page-number",
                    QueryParams = new Dictionary<string, string>
                    {
                        ["page"] = "Page number",
                        ["pageSize"] = "Items per page"
                    },
                    ResponseSchema = DetectPaginationResponse(endpoint)
                });
            }
            // Check for cursor-based pagination
            else if (queryParams.Contains("cursor") || queryParams.Contains("next"))
            {
                patterns.Add(new PaginationPattern
                {
                    Type = "Pagination",
                    Confidence = 0.85,
                    Mechanism = "cursor",
                    QueryParams = new Dictionary<string, string>
                    {
                        ["cursor"] = "Cursor for next page"
                    },
                    ResponseSchema = DetectPaginationResponse(endpoint)
                });
            }
        }

        return patterns;
    }

    /// <summary>
    /// Detects filtering patterns
    /// </summary>
    private static List<FilteringPattern> DetectFiltering(List<EndpointInfo> endpoints)
    {
        var patterns = new List<FilteringPattern>();

        foreach (var endpoint in endpoints.Where(e => e.HttpVerb == "GET"))
        {
            var filterParams = endpoint.QueryParameters
                .Where(p => p.Name.Contains("filter", StringComparison.OrdinalIgnoreCase) ||
                           p.Name.Contains("search", StringComparison.OrdinalIgnoreCase) ||
                           p.Name.Equals("q", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Name)
                .ToList();

            if (filterParams.Any())
            {
                patterns.Add(new FilteringPattern
                {
                    Type = "Filtering",
                    Confidence = 0.85,
                    QueryParams = filterParams
                });
            }
        }

        return patterns;
    }

    /// <summary>
    /// Detects batch operation patterns
    /// </summary>
    private static List<BatchOperationPattern> DetectBatchOperations(List<EndpointInfo> endpoints)
    {
        var patterns = new List<BatchOperationPattern>();

        foreach (var endpoint in endpoints)
        {
            if (endpoint.BodySchema?.Fields != null)
            {
                // Look for array fields that suggest batch operations
                var arrayFields = endpoint.BodySchema.Fields
                    .Where(f => f.Value.IsArray)
                    .Select(f => f.Key)
                    .ToList();

                foreach (var arrayField in arrayFields)
                {
                    if (endpoint.Endpoint.Contains("batch", StringComparison.OrdinalIgnoreCase) ||
                        endpoint.Summary.Contains("bulk", StringComparison.OrdinalIgnoreCase) ||
                        endpoint.Summary.Contains("batch", StringComparison.OrdinalIgnoreCase))
                    {
                        patterns.Add(new BatchOperationPattern
                        {
                            Type = "BatchOperation",
                            Confidence = 0.9,
                            Endpoint = endpoint.Endpoint,
                            HttpVerb = endpoint.HttpVerb,
                            ArrayField = arrayField
                        });
                    }
                }
            }
        }

        return patterns;
    }

    /// <summary>
    /// Detects pagination response schema
    /// </summary>
    private static PaginationResponseSchema DetectPaginationResponse(EndpointInfo endpoint)
    {
        var response = endpoint.Responses.GetValueOrDefault(200);
        if (response?.Fields == null)
        {
            return new PaginationResponseSchema
            {
                DataField = "items"
            };
        }

        var dataField = response.Fields.Keys
            .FirstOrDefault(k => k.Contains("items", StringComparison.OrdinalIgnoreCase) ||
                               k.Contains("data", StringComparison.OrdinalIgnoreCase) ||
                               k.Contains("results", StringComparison.OrdinalIgnoreCase)) ?? "items";

        var totalField = response.Fields.Keys
            .FirstOrDefault(k => k.Contains("total", StringComparison.OrdinalIgnoreCase) ||
                               k.Contains("count", StringComparison.OrdinalIgnoreCase));

        var hasNextField = response.Fields.Keys
            .FirstOrDefault(k => k.Contains("hasnext", StringComparison.OrdinalIgnoreCase) ||
                               k.Contains("has_more", StringComparison.OrdinalIgnoreCase));

        return new PaginationResponseSchema
        {
            DataField = dataField,
            TotalField = totalField,
            HasNextField = hasNextField
        };
    }

    /// <summary>
    /// Gets base path without ID parameters
    /// </summary>
    private static string GetBasePath(string path)
    {
        // Remove path segments containing parameters like {id}, {customerId}, etc.
        // This groups /api/accounts and /api/accounts/{id} together
        return System.Text.RegularExpressions.Regex.Replace(path, @"/\{[^}]+\}", "");
    }

    /// <summary>
    /// Extracts resource name from path
    /// </summary>
    private static string ExtractResourceName(string path)
    {
        // Get the last path segment before {id}
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var resourcePart = parts.LastOrDefault(p => !p.Contains('{'));
        return resourcePart ?? "resource";
    }
}
