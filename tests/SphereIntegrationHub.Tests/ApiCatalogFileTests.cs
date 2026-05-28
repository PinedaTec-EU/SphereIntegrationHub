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

    [Fact]
    public void Deserialize_ReadsAssertionFailuresBlock()
    {
        var catalog = ApiCatalogFile.Deserialize("""
        - version: "1.0"
          assertionFailuresBlock: false
          definitions: []
        """, ApiCatalogFormat.Yaml);

        var version = Assert.Single(catalog);
        Assert.False(version.AssertionFailuresBlock);
    }

    [Fact]
    public void Deserialize_ReadsLatencyProfiles()
    {
        var catalog = ApiCatalogFile.Deserialize("""
        - version: "1.0"
          latencyProfiles:
            - name: semaphore-default
              bands:
                - name: green
                  minMs: 0
                  maxMs: 200
                  color: green
                - name: amber
                  minMs: 201
                  maxMs: 500
                  color: amber
          definitions:
            - name: accounts
              latencyProfile: semaphore-default
              swaggerUrl: https://example.test/swagger.json
              baseUrl:
                local: https://example.test
        """, ApiCatalogFormat.Yaml);

        var version = Assert.Single(catalog);
        var profile = Assert.Single(version.LatencyProfiles!);
        Assert.Equal("semaphore-default", profile.Name);
        Assert.Equal("amber", profile.Bands[1].Name);
        Assert.Equal("semaphore-default", Assert.Single(version.Definitions).LatencyProfile);
    }

    [Fact]
    public void Deserialize_FailsWhenLatencyBandsOverlap()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ApiCatalogFile.Deserialize("""
        - version: "1.0"
          latencyProfiles:
            - name: semaphore-default
              bands:
                - name: green
                  minMs: 0
                  maxMs: 200
                - name: amber
                  minMs: 200
                  maxMs: 500
          definitions: []
        """, ApiCatalogFormat.Yaml));

        Assert.Contains("overlapping bands", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
