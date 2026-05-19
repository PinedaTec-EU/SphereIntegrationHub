namespace SphereIntegrationHub.Definitions;

public sealed class ApiDefinition
{
    public required string Name { get; set; }
    public string? ContractType { get; set; }
    public string? OpenApiUrl { get; set; }
    public string? SwaggerUrl { get; set; }
    public string? ScalaUrl { get; set; }
    public string? HealthCheck { get; set; }
    public ApiReadinessPolicyDefinition? Readiness { get; set; }
    public string? LatencyProfile { get; set; }
    public Dictionary<string, string>? BaseUrl { get; set; }
    public int? Port { get; set; }
    public string? BasePath { get; set; }
    public string? ApiKey { get; set; }
    public string? ApiKeySecret { get; set; }

    public string GetResolvedContractType()
    {
        if (!string.IsNullOrWhiteSpace(ContractType))
        {
            return NormalizeContractType(ContractType);
        }

        if (!string.IsNullOrWhiteSpace(OpenApiUrl))
        {
            return ApiContractTypes.OpenApi;
        }

        if (!string.IsNullOrWhiteSpace(ScalaUrl))
        {
            return ApiContractTypes.Scala;
        }

        return ApiContractTypes.Swagger;
    }

    public string GetResolvedContractUrl()
    {
        return GetResolvedContractType() switch
        {
            ApiContractTypes.OpenApi => FirstNonEmpty(OpenApiUrl, SwaggerUrl, ScalaUrl),
            ApiContractTypes.Scala => FirstNonEmpty(ScalaUrl, OpenApiUrl, SwaggerUrl),
            ApiContractTypes.Llm => throw new InvalidOperationException("LLM API definitions do not define an OpenAPI contract URL."),
            _ => FirstNonEmpty(SwaggerUrl, OpenApiUrl, ScalaUrl)
        };
    }

    public void SetContractUrl(string contractUrl)
    {
        var normalizedType = GetResolvedContractType();
        switch (normalizedType)
        {
            case ApiContractTypes.OpenApi:
                OpenApiUrl = contractUrl;
                break;
            case ApiContractTypes.Scala:
                ScalaUrl = contractUrl;
                break;
            default:
                SwaggerUrl = contractUrl;
                break;
        }
    }

    private static string NormalizeContractType(string contractType)
    {
        var normalized = contractType.Trim().Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        return normalized switch
        {
            "openapi" => ApiContractTypes.OpenApi,
            "swagger" => ApiContractTypes.Swagger,
            "scala" => ApiContractTypes.Scala,
            "llm" => ApiContractTypes.Llm,
            _ => contractType.Trim()
        };
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        throw new InvalidOperationException("API definition does not define any contract URL.");
    }
}

public static class ApiContractTypes
{
    public const string OpenApi = "openapi";
    public const string Swagger = "swagger";
    public const string Scala = "scala";
    public const string Llm = "llm";
}

public sealed class ApiReadinessPolicyDefinition
{
    public int? MaxRetries { get; set; }
    public int? DelayMs { get; set; }
    public int? TimeoutMs { get; set; }
    public int[]? HttpStatus { get; set; }
}
