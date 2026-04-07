using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.cli;

internal sealed class CliPathResolver : ICliPathResolver
{
    private const string WorkflowsFolderName = "workflows";
    private const string ApiCatalogFileName = "api-catalog.json";
    private const string CacheFolderName = "cache";

    public string FormatPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var fullPath = Path.GetFullPath(path);
        return Path.GetRelativePath(Directory.GetCurrentDirectory(), fullPath);
    }

    public string? ResolveVarsFilePath(string? varsFilePath, string workflowPath, out string? message, out string? error)
    {
        message = null;
        error = null;

        if (!string.IsNullOrWhiteSpace(varsFilePath))
        {
            if (!HasWfvarsExtension(varsFilePath))
            {
                error = "Vars file must use the .wfvars extension.";
                return null;
            }

            var fullPath = Path.GetFullPath(varsFilePath);
            if (!File.Exists(fullPath))
            {
                error = $"Vars file was not found: {FormatPath(fullPath)}";
                return null;
            }

            message = $"Vars file: {FormatPath(fullPath)}";
            return fullPath;
        }

        var directory = Path.GetDirectoryName(workflowPath) ?? string.Empty;
        var workflowName = Path.GetFileNameWithoutExtension(workflowPath);
        if (string.IsNullOrWhiteSpace(workflowName))
        {
            return null;
        }

        var defaultPath = Path.Combine(directory, $"{workflowName}.wfvars");
        if (File.Exists(defaultPath))
        {
            message = $"Vars file: {FormatPath(defaultPath)} (auto)";
            return defaultPath;
        }

        return null;
    }

    public string ResolveDefaultCatalogPath(string? workflowPath)
    {
        var resourcesRoot = ResolveDefaultResourcesRoot(workflowPath);
        return Path.Combine(resourcesRoot ?? AppContext.BaseDirectory, ApiCatalogFileName);
    }

    public string ResolveDefaultCacheRoot(string? workflowPath)
    {
        var resourcesRoot = ResolveDefaultResourcesRoot(workflowPath);
        return Path.Combine(resourcesRoot ?? AppContext.BaseDirectory, CacheFolderName);
    }

    private static bool HasWfvarsExtension(string path)
    {
        return string.Equals(Path.GetExtension(path), WorkflowConstants.ExtWfvars, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveDefaultResourcesRoot(string? workflowPath)
    {
        if (string.IsNullOrWhiteSpace(workflowPath))
        {
            return null;
        }

        var workflowDirectoryPath = Path.GetDirectoryName(Path.GetFullPath(workflowPath));
        if (string.IsNullOrWhiteSpace(workflowDirectoryPath))
        {
            return null;
        }

        var directory = new DirectoryInfo(workflowDirectoryPath);
        while (directory is not null)
        {
            if (string.Equals(directory.Name, WorkflowsFolderName, StringComparison.OrdinalIgnoreCase) &&
                directory.Parent is not null)
            {
                return directory.Parent.FullName;
            }

            if (File.Exists(Path.Combine(directory.FullName, ApiCatalogFileName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        var workflowDirectory = Path.GetDirectoryName(workflowPath);
        if (string.IsNullOrWhiteSpace(workflowDirectory))
        {
            return null;
        }

        return Directory.GetParent(workflowDirectory)?.FullName;
    }
}
