namespace SphereIntegrationHub.Definitions;

public sealed record PayloadProcessorContext(
    long Index,
    string TemplateFilePath,
    string TemplateFileName,
    string TemplateDirectory,
    string TemplateFileNameWithoutExtension);
