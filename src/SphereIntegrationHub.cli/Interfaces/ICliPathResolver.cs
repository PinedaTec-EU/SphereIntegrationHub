namespace SphereIntegrationHub.cli;

internal interface ICliPathResolver
{
    string FormatPath(string? path);
    string? ResolveVarsFilePath(string? varsFilePath, string workflowPath, out string? message, out string? error);
    string ResolveDefaultCatalogPath(string? workflowPath);
    string ResolveDefaultCacheRoot(string? workflowPath);
}
