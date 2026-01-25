namespace SphereIntegrationHub.cli;

internal sealed class CliPathResolver : ICliPathResolver
{
    public string FormatPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var fullPath = Path.GetFullPath(path);
        return Path.GetRelativePath(AppContext.BaseDirectory, fullPath);
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
        if (!string.IsNullOrWhiteSpace(workflowPath))
        {
            var workflowDirectory = Path.GetDirectoryName(workflowPath);
            if (!string.IsNullOrWhiteSpace(workflowDirectory))
            {
                var parent = Directory.GetParent(workflowDirectory);
                if (parent is not null)
                {
                    return Path.Combine(parent.FullName, "api-catalog.json");
                }
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "api-catalog.json");
    }

    public string ResolveDefaultCacheRoot(string? workflowPath)
    {
        if (!string.IsNullOrWhiteSpace(workflowPath))
        {
            var workflowDirectory = Path.GetDirectoryName(workflowPath);
            if (!string.IsNullOrWhiteSpace(workflowDirectory))
            {
                var parent = Directory.GetParent(workflowDirectory);
                if (parent is not null)
                {
                    return Path.Combine(parent.FullName, "cache");
                }
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "cache");
    }

    private static bool HasWfvarsExtension(string path)
    {
        return string.Equals(Path.GetExtension(path), ".wfvars", StringComparison.OrdinalIgnoreCase);
    }
}
