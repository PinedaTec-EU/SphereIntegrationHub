using System.Text.Json;

using SphereIntegrationHub.Services;

namespace SphereIntegrationHub.Tests;

public sealed class WorkflowExecutionRedactorTests
{
    // ------------------------------------------------------------------ ConvertOutputs — no secrets

    [Fact]
    public void ConvertOutputs_WithNoSecrets_ReturnsAllValuesUnmasked()
    {
        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = "alice",
            ["status"] = "active"
        };

        var result = WorkflowExecutionRedactor.ConvertOutputs(outputs);

        Assert.Equal("alice", result["name"]);
        Assert.Equal("active", result["status"]);
    }

    // ------------------------------------------------------------------ ConvertOutputs — secretKeys

    [Fact]
    public void ConvertOutputs_WithSecretKey_MasksMatchingOutputValue()
    {
        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["accessToken"] = "eyJhbGciOiJSUzI1NiJ9.payload.sig",
            ["userId"] = "usr-001"
        };
        var secretKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "accessToken" };

        var result = WorkflowExecutionRedactor.ConvertOutputs(outputs, secretKeys: secretKeys);

        Assert.Equal("*****", result["accessToken"]);
        Assert.Equal("usr-001", result["userId"]);
    }

    [Fact]
    public void ConvertOutputs_SecretKeyMatch_IsCaseInsensitive()
    {
        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AccessToken"] = "super-secret"
        };
        var secretKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "accesstoken" };

        var result = WorkflowExecutionRedactor.ConvertOutputs(outputs, secretKeys: secretKeys);

        Assert.Equal("*****", result["AccessToken"]);
    }

    [Fact]
    public void ConvertOutputs_WithMultipleSecretKeys_MasksAllMatching()
    {
        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["accessToken"] = "token-value",
            ["refreshToken"] = "refresh-value",
            ["userId"] = "usr-001"
        };
        var secretKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "accessToken", "refreshToken" };

        var result = WorkflowExecutionRedactor.ConvertOutputs(outputs, secretKeys: secretKeys);

        Assert.Equal("*****", result["accessToken"]);
        Assert.Equal("*****", result["refreshToken"]);
        Assert.Equal("usr-001", result["userId"]);
    }

    [Fact]
    public void ConvertOutputs_SecretKey_NotInOutputs_DoesNotThrow()
    {
        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["userId"] = "usr-001"
        };
        var secretKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "accessToken" };

        var result = WorkflowExecutionRedactor.ConvertOutputs(outputs, secretKeys: secretKeys);

        Assert.Equal("usr-001", result["userId"]);
        Assert.False(result.ContainsKey("accessToken"));
    }

    // ------------------------------------------------------------------ ConvertOutputs — secretValues

    [Fact]
    public void ConvertOutputs_WithSecretValue_MasksOutputContainingThatValue()
    {
        const string secretNonce = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";
        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["nonce"] = secretNonce,
            ["userId"] = "usr-001"
        };
        var secretValues = new HashSet<string>(StringComparer.Ordinal) { secretNonce };

        var result = WorkflowExecutionRedactor.ConvertOutputs(outputs, secretValues: secretValues);

        Assert.Equal("*****", result["nonce"]);
        Assert.Equal("usr-001", result["userId"]);
    }

    [Fact]
    public void ConvertOutputs_WithSecretValueEmbeddedInComposedString_MasksOnlyTheSecretFragment()
    {
        const string secretValue = "super-secret-token";
        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["combined"] = $"visible=usr-001; secret={secretValue}"
        };
        var secretValues = new HashSet<string>(StringComparer.Ordinal) { secretValue };

        var result = WorkflowExecutionRedactor.ConvertOutputs(outputs, secretValues: secretValues);

        Assert.Equal("visible=usr-001; secret=*****", result["combined"]);
    }

    [Fact]
    public void ConvertOutputs_WithSecretValueEmbeddedInJson_MasksOnlyTheSecretFragment()
    {
        const string secretValue = "super-secret-token";
        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["payload"] = $$"""{"combined":"visible=usr-001; secret={{secretValue}}"}"""
        };
        var secretValues = new HashSet<string>(StringComparer.Ordinal) { secretValue };

        var result = WorkflowExecutionRedactor.ConvertOutputs(outputs, secretValues: secretValues);
        var payload = Assert.IsType<JsonElement>(result["payload"]);

        Assert.Equal("visible=usr-001; secret=*****", payload.GetProperty("combined").GetString());
    }

    [Fact]
    public void ConvertOutputs_SecretValue_DoesNotMaskUnrelatedOutputs()
    {
        const string secretValue = "super-secret-token";
        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["data"] = "unrelated-value",
            ["status"] = "active"
        };
        var secretValues = new HashSet<string>(StringComparer.Ordinal) { secretValue };

        var result = WorkflowExecutionRedactor.ConvertOutputs(outputs, secretValues: secretValues);

        Assert.Equal("unrelated-value", result["data"]);
        Assert.Equal("active", result["status"]);
    }

    [Fact]
    public void ConvertOutputs_EmptySecretValue_IsNotMasked()
    {
        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["token"] = ""
        };
        var secretValues = new HashSet<string>(StringComparer.Ordinal) { "" };

        var result = WorkflowExecutionRedactor.ConvertOutputs(outputs, secretValues: secretValues);

        // Empty strings in secret register should not trigger masking (guarded by !string.IsNullOrEmpty check on registration)
        Assert.Equal("", result["token"]);
    }

    // ------------------------------------------------------------------ ConvertOutputs — combined secretKeys + secretValues

    [Fact]
    public void ConvertOutputs_WithBothSecretKeysAndValues_MasksFromEitherSource()
    {
        const string secretNonce = "nonce-abc-123";
        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["accessToken"] = "jwt.payload.sig",   // masked by key
            ["generatedNonce"] = secretNonce,       // masked by value
            ["userId"] = "usr-001"                  // not masked
        };
        var secretKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "accessToken" };
        var secretValues = new HashSet<string>(StringComparer.Ordinal) { secretNonce };

        var result = WorkflowExecutionRedactor.ConvertOutputs(outputs, secretKeys, secretValues);

        Assert.Equal("*****", result["accessToken"]);
        Assert.Equal("*****", result["generatedNonce"]);
        Assert.Equal("usr-001", result["userId"]);
    }

    // ------------------------------------------------------------------ ConvertOutputs — JSON values

    [Fact]
    public void ConvertOutputs_NonSecretJsonValue_IsDeserializedAsJsonElement()
    {
        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["body"] = "{\"id\":\"usr-001\"}"
        };

        var result = WorkflowExecutionRedactor.ConvertOutputs(outputs);

        Assert.IsNotType<string>(result["body"]);
    }

    [Fact]
    public void ConvertOutputs_SecretKeyWithJsonValue_IsMaskedAsString()
    {
        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["payload"] = "{\"token\":\"secret\"}"
        };
        var secretKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "payload" };

        var result = WorkflowExecutionRedactor.ConvertOutputs(outputs, secretKeys: secretKeys);

        Assert.Equal("*****", result["payload"]);
    }

    // ------------------------------------------------------------------ ConvertOutputs — null / empty guard

    [Fact]
    public void ConvertOutputs_NullSecretKeys_DoesNotMaskAnything()
    {
        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["accessToken"] = "real-value"
        };

        var result = WorkflowExecutionRedactor.ConvertOutputs(outputs, secretKeys: null, secretValues: null);

        Assert.Equal("real-value", result["accessToken"]);
    }

    [Fact]
    public void ConvertOutputs_EmptyOutputs_ReturnsEmptyDictionary()
    {
        var result = WorkflowExecutionRedactor.ConvertOutputs(
            new Dictionary<string, string>(),
            new HashSet<string> { "any" },
            new HashSet<string> { "any" });

        Assert.Empty(result);
    }
}
