using SphereIntegrationHub.MCP.Models;
using SphereIntegrationHub.MCP.Services.Catalog;
using SphereIntegrationHub.MCP.Services.Integration;

namespace SphereIntegrationHub.MCP.Services.Semantic;

/// <summary>
/// Analyzes Swagger specifications to detect dependencies and data flow
/// </summary>
public sealed class SwaggerSemanticAnalyzer
{
    private readonly SihServicesAdapter _adapter;
    private readonly SwaggerReader _swaggerReader;

    public SwaggerSemanticAnalyzer(SihServicesAdapter adapter)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _swaggerReader = new SwaggerReader(adapter);
    }

    /// <summary>
    /// Analyzes dependencies for a target endpoint
    /// </summary>
    public async Task<EndpointDependencies> AnalyzeDependenciesAsync(
        string version,
        string apiName,
        string endpoint,
        string httpVerb)
    {
        // Get the target endpoint schema
        var targetEndpoint = await _swaggerReader.GetEndpointSchemaAsync(version, apiName, endpoint, httpVerb);
        if (targetEndpoint == null)
        {
            throw new InvalidOperationException($"Endpoint not found: {httpVerb} {endpoint} in {apiName}");
        }

        // Extract all required fields from the target endpoint
        var requiredFields = new List<RequiredField>();

        // Add required path parameters
        foreach (var param in targetEndpoint.PathParameters.Where(p => p.Required))
        {
            requiredFields.Add(new RequiredField
            {
                Field = param.Name,
                Type = param.Type,
                Location = "path",
                PossibleSources = []
            });
        }

        // Add required query parameters
        foreach (var param in targetEndpoint.QueryParameters.Where(p => p.Required))
        {
            requiredFields.Add(new RequiredField
            {
                Field = param.Name,
                Type = param.Type,
                Location = "query",
                PossibleSources = []
            });
        }

        // Add required header parameters
        foreach (var param in targetEndpoint.HeaderParameters.Where(p => p.Required))
        {
            requiredFields.Add(new RequiredField
            {
                Field = param.Name,
                Type = param.Type,
                Location = "header",
                PossibleSources = []
            });
        }

        // Add required body fields
        if (targetEndpoint.BodySchema != null)
        {
            foreach (var fieldName in targetEndpoint.BodySchema.RequiredFields)
            {
                if (targetEndpoint.BodySchema.Fields.TryGetValue(fieldName, out var field))
                {
                    requiredFields.Add(new RequiredField
                    {
                        Field = fieldName,
                        Type = field.Type,
                        Location = "body",
                        PossibleSources = []
                    });
                }
            }
        }

        // Find possible sources for each required field
        var catalogReader = new ApiCatalogReader(_adapter);
        var allApis = await catalogReader.GetApiDefinitionsAsync(version);

        foreach (var requiredField in requiredFields)
        {
            requiredField.PossibleSources.AddRange(
                await FindFieldSourcesAsync(version, allApis, requiredField, apiName, endpoint));
        }

        // Generate suggested execution order
        var executionOrder = BuildExecutionOrder(requiredFields);

        return new EndpointDependencies
        {
            Endpoint = endpoint,
            HttpVerb = httpVerb,
            ApiName = apiName,
            RequiredFields = requiredFields,
            SuggestedExecutionOrder = executionOrder
        };
    }

    /// <summary>
    /// Finds endpoints that can provide a specific field
    /// </summary>
    private async Task<List<FieldSource>> FindFieldSourcesAsync(
        string version,
        List<ApiDefinition> apis,
        RequiredField requiredField,
        string targetApiName,
        string targetEndpoint)
    {
        var sources = new List<FieldSource>();

        foreach (var api in apis)
        {
            try
            {
                var endpoints = await _swaggerReader.GetEndpointsAsync(version, api.Name);

                foreach (var endpoint in endpoints)
                {
                    // Skip the target endpoint itself
                    if (endpoint.ApiName == targetApiName && endpoint.Endpoint == targetEndpoint)
                    {
                        continue;
                    }

                    // Check if this endpoint returns data (typically GET requests or POST that create resources)
                    if (endpoint.Responses.TryGetValue(200, out var response) && response.Fields != null)
                    {
                        foreach (var (fieldName, fieldSchema) in response.Fields)
                        {
                            var confidence = CalculateConfidence(requiredField, fieldName, fieldSchema, endpoint);
                            if (confidence > 0.3) // Only include matches with >30% confidence
                            {
                                sources.Add(new FieldSource
                                {
                                    Endpoint = endpoint.Endpoint,
                                    HttpVerb = endpoint.HttpVerb,
                                    ApiName = endpoint.ApiName,
                                    ResponseField = fieldName,
                                    Confidence = confidence,
                                    Reasoning = GenerateReasoning(requiredField, fieldName, fieldSchema, confidence)
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Log but continue processing other APIs
                Console.Error.WriteLine($"[SwaggerSemanticAnalyzer] Error analyzing {api.Name}: {ex.Message}");
            }
        }

        // Sort by confidence descending
        return sources.OrderByDescending(s => s.Confidence).ToList();
    }

    /// <summary>
    /// Calculates confidence score for a field match (0.0 to 1.0)
    /// </summary>
    private static double CalculateConfidence(
        RequiredField required,
        string candidateFieldName,
        FieldSchema candidateSchema,
        EndpointInfo sourceEndpoint)
    {
        var confidence = 0.0;

        // Exact name match
        if (candidateFieldName.Equals(required.Field, StringComparison.OrdinalIgnoreCase))
        {
            confidence += 0.5;
        }
        // Partial name match (e.g., "customerId" matches "customer_id" or "id")
        else if (NormalizeFieldName(candidateFieldName) == NormalizeFieldName(required.Field))
        {
            confidence += 0.4;
        }
        // Contains the field name
        else if (candidateFieldName.Contains(required.Field, StringComparison.OrdinalIgnoreCase) ||
                 required.Field.Contains(candidateFieldName, StringComparison.OrdinalIgnoreCase))
        {
            confidence += 0.3;
        }

        // Type match
        if (candidateSchema.Type.Equals(required.Type, StringComparison.OrdinalIgnoreCase))
        {
            confidence += 0.3;
        }

        // Common patterns
        if (required.Field.EndsWith("Id", StringComparison.OrdinalIgnoreCase) &&
            (sourceEndpoint.HttpVerb == "POST" || sourceEndpoint.HttpVerb == "GET") &&
            candidateFieldName.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
        {
            confidence += 0.2;
        }

        return Math.Min(confidence, 1.0);
    }

    /// <summary>
    /// Normalizes field names for comparison (removes underscores, converts to lowercase)
    /// </summary>
    private static string NormalizeFieldName(string fieldName)
    {
        return fieldName.Replace("_", "").Replace("-", "").ToLowerInvariant();
    }

    /// <summary>
    /// Generates human-readable reasoning for a match
    /// </summary>
    private static string GenerateReasoning(
        RequiredField required,
        string candidateFieldName,
        FieldSchema candidateSchema,
        double confidence)
    {
        var reasons = new List<string>();

        if (candidateFieldName.Equals(required.Field, StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("exact name match");
        }
        else if (NormalizeFieldName(candidateFieldName) == NormalizeFieldName(required.Field))
        {
            reasons.Add("name match (normalized)");
        }
        else if (candidateFieldName.Contains(required.Field, StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("partial name match");
        }

        if (candidateSchema.Type.Equals(required.Type, StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("type match");
        }

        return reasons.Any()
            ? string.Join(", ", reasons)
            : $"weak match (confidence: {confidence:P0})";
    }

    /// <summary>
    /// Builds suggested execution order based on dependencies
    /// </summary>
    private static List<ExecutionStep> BuildExecutionOrder(List<RequiredField> requiredFields)
    {
        var executionOrder = new List<ExecutionStep>();
        var step = 1;

        // Group by unique source endpoints
        var uniqueSources = requiredFields
            .SelectMany(f => f.PossibleSources)
            .Where(s => s.Confidence > 0.5)
            .GroupBy(s => $"{s.ApiName}:{s.Endpoint}:{s.HttpVerb}")
            .Select(g => g.First())
            .OrderByDescending(s => s.Confidence)
            .ToList();

        foreach (var source in uniqueSources)
        {
            var providedFields = requiredFields
                .Where(f => f.PossibleSources.Any(s =>
                    s.ApiName == source.ApiName &&
                    s.Endpoint == source.Endpoint &&
                    s.HttpVerb == source.HttpVerb))
                .Select(f => f.Field)
                .ToList();

            if (providedFields.Any())
            {
                executionOrder.Add(new ExecutionStep
                {
                    Step = step++,
                    Endpoint = source.Endpoint,
                    HttpVerb = source.HttpVerb,
                    ApiName = source.ApiName,
                    Reason = $"Provides: {string.Join(", ", providedFields)}"
                });
            }
        }

        return executionOrder;
    }
}
