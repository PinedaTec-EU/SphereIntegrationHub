using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.Tests;

public sealed class ApiCatalogFileTests
{
    [Fact]
    public void Deserialize_ReadsConnections()
    {
        var catalog = ApiCatalogFile.Deserialize("""
        - version: "1.0"
          definitions: []
          connections:
            - name: openai-main
              type: llm
              provider: openai
              baseUrl:
                local: https://api.openai.com/v1
              apiKeySecret: "{{input.openaiApiKey}}"
        """, ApiCatalogFormat.Yaml);

        var version = Assert.Single(catalog);
        var connection = Assert.Single(version.Connections!);
        Assert.Equal("openai-main", connection.Name);
        Assert.Equal("llm", connection.Type);
        Assert.Equal("openai", connection.Provider);
        Assert.Equal("https://api.openai.com/v1", connection.BaseUrl!["local"]);
        Assert.Equal("{{input.openaiApiKey}}", connection.ApiKeySecret);
    }
}
