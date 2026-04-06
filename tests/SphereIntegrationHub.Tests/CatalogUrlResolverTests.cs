using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;

namespace SphereIntegrationHub.Tests;

public sealed class CatalogUrlResolverTests
{
    [Fact]
    public void TryResolveBaseUrl_WithDefinitionBaseUrlAndPort_AppliesPort()
    {
        var version = new ApiCatalogVersion
        {
            Version = "0.1",
            Definitions = []
        };

        var definition = new ApiDefinition
        {
            Name = "TravelAgent.Admin.Api",
            SwaggerUrl = "/swagger/v1/swagger.json",
            Port = 5009,
            BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["local"] = "https://localhost"
            }
        };

        var resolved = ApiBaseUrlResolver.TryResolveBaseUrl(version, definition, "local", out var baseUrl);

        Assert.True(resolved);
        Assert.Equal("https://localhost:5009", baseUrl);
    }

    [Fact]
    public void ResolveSwaggerUri_WithDefinitionBaseUrlTemplateAndPort_ReturnsExpandedAbsoluteUri()
    {
        var version = new ApiCatalogVersion
        {
            Version = "0.1",
            Definitions = []
        };

        var definition = new ApiDefinition
        {
            Name = "TravelAgent.Admin.Api",
            SwaggerUrl = "{{baseUrl.local}}:{{port}}/swagger/v1/swagger.json",
            Port = 5009,
            BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["local"] = "https://localhost"
            }
        };

        var swaggerUri = CatalogUrlResolver.ResolveSwaggerUri(version, definition, "local");

        Assert.Equal("https://localhost:5009/swagger/v1/swagger.json", swaggerUri.ToString());
    }

    [Fact]
    public void ResolveSwaggerUri_WithRelativeSwaggerPathAndDefinitionBaseUrl_ReturnsAbsoluteUri()
    {
        var version = new ApiCatalogVersion
        {
            Version = "0.1",
            Definitions = []
        };

        var definition = new ApiDefinition
        {
            Name = "TravelAgent.Admin.Api",
            SwaggerUrl = "/swagger/v1/swagger.json",
            BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["local"] = "http://localhost:5004"
            }
        };

        var swaggerUri = CatalogUrlResolver.ResolveSwaggerUri(version, definition, "local");

        Assert.Equal("http://localhost:5004/swagger/v1/swagger.json", swaggerUri.ToString());
        Assert.False(swaggerUri.IsFile);
    }

    [Fact]
    public void ResolveHealthCheckUri_WithDefinitionBaseUrlAndPort_ReturnsExpandedAbsoluteUri()
    {
        var version = new ApiCatalogVersion
        {
            Version = "0.1",
            Definitions = []
        };

        var definition = new ApiDefinition
        {
            Name = "TravelAgent.Admin.Api",
            SwaggerUrl = "/swagger/v1/swagger.json",
            HealthCheck = "/health",
            Port = 5009,
            BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["local"] = "https://localhost"
            }
        };

        var healthCheckUri = CatalogUrlResolver.ResolveHealthCheckUri(version, definition, "local");

        Assert.Equal("https://localhost:5009/health", healthCheckUri.ToString());
    }
}
