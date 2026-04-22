using System.Text.Json;

namespace SphereIntegrationHub.Definitions;

public sealed record ResponseContext(
    int StatusCode,
    string Body,
    IReadOnlyDictionary<string, string> Headers,
    JsonDocument? Json);
