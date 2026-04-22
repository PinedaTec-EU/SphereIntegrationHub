using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.VaultwardenPlugin;

using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace SphereIntegrationHub.Tests;

public sealed class VaultwardenSecretProviderPluginTests
{
    [Fact]
    public async Task ResolveAsync_AuthenticatesAndMapsSecrets()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/identity/connect/token").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"access_token":"vw-token"}"""));
        server
            .Given(Request.Create().WithPath("/api/sih/secrets").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
{
  "secrets": {
    "accounts.api-token": "token-123",
    "accounts.client-secret": "secret-456"
  }
}
"""));

        var plugin = new VaultwardenSecretProviderPlugin();
        var definition = new SecretProviderDefinition
        {
            Plugin = "vaultwarden",
            Config = new Dictionary<string, object?>
            {
                ["baseUrl"] = $"http://localhost:{server.Ports[0]}",
                ["usernameEnv"] = "VAULTWARDEN_USERNAME",
                ["passwordEnv"] = "VAULTWARDEN_PASSWORD",
                ["mappings"] = new Dictionary<string, object?>
                {
                    ["ACCOUNTS_API_TOKEN"] = "accounts.api-token",
                    ["ACCOUNTS_CLIENT_SECRET"] = "accounts.client-secret"
                }
            }
        };

        using var httpClient = new HttpClient();
        var result = await plugin.ResolveAsync(
            definition,
            new SecretProviderExecutionContext(
                httpClient,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["VAULTWARDEN_USERNAME"] = "demo@example.test",
                    ["VAULTWARDEN_PASSWORD"] = "super-secret"
                }),
            CancellationToken.None);

        Assert.Equal("token-123", result.Secrets["ACCOUNTS_API_TOKEN"]);
        Assert.Equal("secret-456", result.Secrets["ACCOUNTS_CLIENT_SECRET"]);
        Assert.Contains("token-123", result.SecretValues);
        Assert.Contains("secret-456", result.SecretValues);
    }

    [Fact]
    public async Task ResolveAsync_FailsWhenMappedSecretIsMissing()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/identity/connect/token").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"access_token":"vw-token"}"""));
        server
            .Given(Request.Create().WithPath("/api/sih/secrets").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
{
  "secrets": {
    "accounts.api-token": "token-123"
  }
}
"""));

        var plugin = new VaultwardenSecretProviderPlugin();
        var definition = new SecretProviderDefinition
        {
            Plugin = "vaultwarden",
            Config = new Dictionary<string, object?>
            {
                ["baseUrl"] = $"http://localhost:{server.Ports[0]}",
                ["usernameEnv"] = "VAULTWARDEN_USERNAME",
                ["passwordEnv"] = "VAULTWARDEN_PASSWORD",
                ["mappings"] = new Dictionary<string, object?>
                {
                    ["ACCOUNTS_API_TOKEN"] = "accounts.api-token",
                    ["ACCOUNTS_CLIENT_SECRET"] = "accounts.client-secret"
                }
            }
        };

        using var httpClient = new HttpClient();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => plugin.ResolveAsync(
            definition,
            new SecretProviderExecutionContext(
                httpClient,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["VAULTWARDEN_USERNAME"] = "demo@example.test",
                    ["VAULTWARDEN_PASSWORD"] = "super-secret"
                }),
            CancellationToken.None));

        Assert.Contains("accounts.client-secret", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
