using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.Sdk.Internal;

internal static class SdkPathResolver
{
    private const string WorkflowsFolderName = "workflows";
    private const string CacheFolderName = "cache";

    public static string? ResolveVarsFilePath(string? varsFilePath, string workflowPath)
    {
        if (!string.IsNullOrWhiteSpace(varsFilePath))
        {
            if (!string.Equals(Path.GetExtension(varsFilePath), WorkflowConstants.ExtWfvars, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Vars file must use the .wfvars extension.");
            }

            var fullPath = Path.GetFullPath(varsFilePath);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("Vars file was not found.", fullPath);
            }

            return fullPath;
        }

        var directory = Path.GetDirectoryName(workflowPath) ?? string.Empty;
        var workflowName = Path.GetFileNameWithoutExtension(workflowPath);
        if (string.IsNullOrWhiteSpace(workflowName))
        {
            return null;
        }

        var defaultPath = Path.Combine(directory, $"{workflowName}.wfvars");
        return File.Exists(defaultPath) ? defaultPath : null;
    }

    public static string ResolveDefaultCatalogPath(string workflowPath)
    {
        var resourcesRoot = ResolveDefaultResourcesRoot(workflowPath);
        return ApiCatalogFile.ResolvePath(resourcesRoot ?? AppContext.BaseDirectory);
    }

    public static string ResolveDefaultCacheRoot(string workflowPath)
    {
        var resourcesRoot = ResolveDefaultResourcesRoot(workflowPath);
        return Path.Combine(resourcesRoot ?? AppContext.BaseDirectory, CacheFolderName);
    }

    private static string? ResolveDefaultResourcesRoot(string workflowPath)
    {
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

            var catalogPath = ApiCatalogFile.ResolvePath(directory.FullName);
            if (File.Exists(catalogPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Directory.GetParent(workflowDirectoryPath)?.FullName;
    }
}
