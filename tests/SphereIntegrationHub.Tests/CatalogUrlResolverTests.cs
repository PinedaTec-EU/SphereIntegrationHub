using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;

namespace SphereIntegrationHub.Tests;

public sealed class CatalogUrlResolverTests
{
    [Fact]
    public void TryResolveBaseUrl_WithDefinitionPort_UsesVersionBaseUrlAndPort()
    {
        var version = new ApiCatalogVersion
        {
            Version = "0.1",
            BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["local"] = "https://localhost"
            },
            Definitions = []
        };

        var definition = new ApiDefinition
        {
            Name = "TravelAgent.Admin.Api",
            SwaggerUrl = "/swagger/v1/swagger.json",
            Port = 5009
        };

        var resolved = ApiBaseUrlResolver.TryResolveBaseUrl(version, definition, "local", out var baseUrl);

        Assert.True(resolved);
        Assert.Equal("https://localhost:5009", baseUrl);
    }

    [Fact]
    public void ResolveSwaggerUri_WithTemplateBaseUrlAndPort_ReturnsExpandedAbsoluteUri()
    {
        var version = new ApiCatalogVersion
        {
            Version = "0.1",
            BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["local"] = "https://localhost"
            },
            Definitions = []
        };

        var definition = new ApiDefinition
        {
            Name = "TravelAgent.Admin.Api",
            SwaggerUrl = "{{baseUrl.local}}:{{port}}/swagger/v1/swagger.json",
            Port = 5009
        };

        var swaggerUri = CatalogUrlResolver.ResolveSwaggerUri(version, definition, "local");

        Assert.Equal("https://localhost:5009/swagger/v1/swagger.json", swaggerUri.ToString());
    }
}
