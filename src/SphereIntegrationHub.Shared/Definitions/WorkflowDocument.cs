namespace SphereIntegrationHub.Definitions;

public sealed record WorkflowDocument(
    WorkflowDefinition Definition,
    string FilePath,
    IReadOnlyDictionary<string, string> EnvironmentVariables);
