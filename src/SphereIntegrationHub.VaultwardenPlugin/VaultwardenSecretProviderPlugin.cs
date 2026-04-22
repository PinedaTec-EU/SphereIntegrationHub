using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.VaultwardenPlugin;

public sealed class VaultwardenSecretProviderPlugin : SecretProviderPluginBase
{
    private const string DefaultTokenPath = "/identity/connect/token";
    private const string DefaultSecretsPath = "/api/sih/secrets";
    private const string DefaultClientId = "web";

    public VaultwardenSecretProviderPlugin()
        : base("vaultwarden")
    {
    }

    public override async Task<SecretProviderResult> ResolveAsync(
        SecretProviderDefinition definition,
        SecretProviderExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(context);

        var baseUrl = definition.GetConfigString("baseUrl")
            ?? throw new InvalidOperationException("Vaultwarden secret provider requires config.baseUrl.");
        var username = ResolveRequiredEnvironmentValue(definition, context.ProcessEnvironment, "usernameEnv");
        var password = ResolveRequiredEnvironmentValue(definition, context.ProcessEnvironment, "passwordEnv");
        var clientId = ResolveOptionalEnvironmentValue(definition, context.ProcessEnvironment, "clientIdEnv") ?? DefaultClientId;
        var clientSecret = ResolveOptionalEnvironmentValue(definition, context.ProcessEnvironment, "clientSecretEnv");
        var tokenPath = definition.GetConfigString("tokenPath") ?? DefaultTokenPath;
        var secretsPath = definition.GetConfigString("secretsPath") ?? DefaultSecretsPath;
        var mappings = definition.GetConfigStringDictionary("mappings");
        if (mappings is null || mappings.Count == 0)
        {
            throw new InvalidOperationException("Vaultwarden secret provider requires config.mappings.");
        }

        var accessToken = await RequestAccessTokenAsync(
            context.HttpClient,
            baseUrl,
            tokenPath,
            username,
            password,
            clientId,
            clientSecret,
            cancellationToken);
        var resolvedSecrets = await RequestSecretsAsync(
            context.HttpClient,
            baseUrl,
            secretsPath,
            mappings,
            accessToken,
            cancellationToken);

        return new SecretProviderResult(resolvedSecrets, resolvedSecrets.Values.ToArray());
    }

    private static async Task<string> RequestAccessTokenAsync(
        HttpClient httpClient,
        string baseUrl,
        string tokenPath,
        string username,
        string password,
        string clientId,
        string? clientSecret,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(baseUrl, tokenPath))
        {
            Content = new FormUrlEncodedContent(BuildTokenRequestBody(username, password, clientId, clientSecret))
        };

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Vaultwarden token request failed with status {(int)response.StatusCode}.");
        }

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("access_token", out var accessTokenEl) ||
            string.IsNullOrWhiteSpace(accessTokenEl.GetString()))
        {
            throw new InvalidOperationException("Vaultwarden token response did not contain access_token.");
        }

        return accessTokenEl.GetString()!;
    }

    private static IEnumerable<KeyValuePair<string, string>> BuildTokenRequestBody(
        string username,
        string password,
        string clientId,
        string? clientSecret)
    {
        var body = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "password"),
            new("scope", "api offline_access"),
            new("client_id", clientId),
            new("username", username),
            new("password", password)
        };

        if (!string.IsNullOrWhiteSpace(clientSecret))
        {
            body.Add(new("client_secret", clientSecret));
        }

        return body;
    }

    private static async Task<IReadOnlyDictionary<string, string>> RequestSecretsAsync(
        HttpClient httpClient,
        string baseUrl,
        string secretsPath,
        IReadOnlyDictionary<string, string> mappings,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var requestedNames = mappings.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(baseUrl, secretsPath))
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { names = requestedNames }),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Vaultwarden secrets request failed with status {(int)response.StatusCode}.");
        }

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("secrets", out var secretsEl) ||
            secretsEl.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Vaultwarden secrets response did not contain a secrets object.");
        }

        var availableSecrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in secretsEl.EnumerateObject())
        {
            availableSecrets[property.Name] = property.Value.GetString() ?? string.Empty;
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in mappings)
        {
            if (!availableSecrets.TryGetValue(mapping.Value, out var secretValue))
            {
                throw new InvalidOperationException($"Vaultwarden secret '{mapping.Value}' was not returned by the provider.");
            }

            result[mapping.Key] = secretValue;
        }

        return result;
    }

    private static string ResolveRequiredEnvironmentValue(
        SecretProviderDefinition definition,
        IReadOnlyDictionary<string, string> processEnvironment,
        string envKeyPropertyName)
    {
        var envKey = definition.GetConfigString(envKeyPropertyName)
            ?? throw new InvalidOperationException($"Vaultwarden secret provider requires config.{envKeyPropertyName}.");
        if (!processEnvironment.TryGetValue(envKey, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Environment variable '{envKey}' was not found for Vaultwarden secret provider.");
        }

        return value;
    }

    private static string? ResolveOptionalEnvironmentValue(
        SecretProviderDefinition definition,
        IReadOnlyDictionary<string, string> processEnvironment,
        string envKeyPropertyName)
    {
        var envKey = definition.GetConfigString(envKeyPropertyName);
        if (string.IsNullOrWhiteSpace(envKey))
        {
            return null;
        }

        return processEnvironment.TryGetValue(envKey, out var value) ? value : null;
    }

    private static Uri BuildUri(string baseUrl, string relativeOrAbsolute)
    {
        if (Uri.TryCreate(relativeOrAbsolute, UriKind.Absolute, out var absoluteUri) &&
            (string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return absoluteUri;
        }

        var normalizedBaseUrl = baseUrl.Contains("://", StringComparison.Ordinal)
            ? baseUrl
            : $"http://{baseUrl}";

        return new Uri(new Uri(normalizedBaseUrl.TrimEnd('/') + "/"), relativeOrAbsolute.TrimStart('/'));
    }
}
