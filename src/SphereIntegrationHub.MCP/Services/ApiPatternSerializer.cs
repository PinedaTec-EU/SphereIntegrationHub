using SphereIntegrationHub.MCP.Models;

namespace SphereIntegrationHub.MCP.Services;

internal static class ApiPatternSerializer
{
    public static object Serialize(ApiPattern pattern)
    {
        return pattern switch
        {
            OAuth2Pattern oauth => new
            {
                type = oauth.Type,
                confidence = oauth.Confidence,
                endpoints = oauth.Endpoints,
                grantTypes = oauth.GrantTypes,
                tokenLocation = oauth.TokenLocation
            },
            CrudPattern crud => new
            {
                type = crud.Type,
                confidence = crud.Confidence,
                resource = crud.Resource,
                endpoints = crud.Endpoints,
                idParameter = crud.IdParameter,
                idType = crud.IdType
            },
            PaginationPattern pagination => new
            {
                type = pagination.Type,
                confidence = pagination.Confidence,
                mechanism = pagination.Mechanism,
                queryParams = pagination.QueryParams,
                responseSchema = new
                {
                    dataField = pagination.ResponseSchema.DataField,
                    totalField = pagination.ResponseSchema.TotalField,
                    hasNextField = pagination.ResponseSchema.HasNextField
                }
            },
            FilteringPattern filtering => new
            {
                type = filtering.Type,
                confidence = filtering.Confidence,
                queryParams = filtering.QueryParams
            },
            BatchOperationPattern batch => new
            {
                type = batch.Type,
                confidence = batch.Confidence,
                endpoint = batch.Endpoint,
                httpVerb = batch.HttpVerb,
                arrayField = batch.ArrayField
            },
            _ => new { type = pattern.Type, confidence = pattern.Confidence }
        };
    }

    public static Dictionary<string, object> BuildSummary(ApiPatternCollection patterns)
    {
        return new Dictionary<string, object>
        {
            ["totalPatterns"] = patterns.Patterns.Count,
            ["hasOAuth"] = patterns.Patterns.Any(p => p is OAuth2Pattern),
            ["crudResources"] = patterns.Patterns.OfType<CrudPattern>().Select(p => p.Resource).ToList(),
            ["supportsPagination"] = patterns.Patterns.Any(p => p is PaginationPattern),
            ["supportsFiltering"] = patterns.Patterns.Any(p => p is FilteringPattern),
            ["supportsBatchOperations"] = patterns.Patterns.Any(p => p is BatchOperationPattern)
        };
    }
}
