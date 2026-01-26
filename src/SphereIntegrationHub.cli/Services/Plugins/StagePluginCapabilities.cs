namespace SphereIntegrationHub.Services.Plugins;

public sealed record StagePluginCapabilities(
    StageOutputKind OutputKind,
    StageMockKind MockKind,
    bool AllowsResponseTokens,
    bool SupportsJumpOnStatus,
    bool ContinueOnError);
