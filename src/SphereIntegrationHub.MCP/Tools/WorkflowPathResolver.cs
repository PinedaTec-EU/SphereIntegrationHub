using SphereIntegrationHub.MCP.Services.Integration;

namespace SphereIntegrationHub.MCP.Tools;

internal static class WorkflowPathResolver
{
    public static string ResolveExistingWorkflowPath(SihServicesAdapter adapter, string workflowPath)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        if (string.IsNullOrWhiteSpace(workflowPath))
        {
            throw new ArgumentException("workflowPath is required");
        }

        var resolved = ResolveCandidatePath(adapter, workflowPath);
        if (File.Exists(resolved))
        {
            return resolved;
        }

        foreach (var alias in BuildAliases(workflowPath))
        {
            var aliasPath = ResolveCandidatePath(adapter, alias);
            if (File.Exists(aliasPath))
            {
                return aliasPath;
            }
        }

        throw new FileNotFoundException($"Workflow file not found: {resolved}", resolved);
    }

    public static string ResolvePath(SihServicesAdapter adapter, string workflowPath)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        if (string.IsNullOrWhiteSpace(workflowPath))
        {
            throw new ArgumentException("workflowPath is required");
        }

        return ResolveCandidatePath(adapter, workflowPath);
    }

    private static string ResolveCandidatePath(SihServicesAdapter adapter, string path)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(Path.Combine(adapter.WorkflowsPath, path));
    }

    private static IEnumerable<string> BuildAliases(string path)
    {
        var aliases = new List<string>();
        var normalized = path.Trim();

        if (normalized.EndsWith(".workflow.yaml", StringComparison.OrdinalIgnoreCase))
        {
            aliases.Add(normalized[..^".yaml".Length]);
        }
        else if (normalized.EndsWith(".workflow.yml", StringComparison.OrdinalIgnoreCase))
        {
            aliases.Add(normalized[..^".yml".Length]);
        }
        else if (normalized.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) ||
                 normalized.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
        {
            aliases.Add(Path.ChangeExtension(normalized, ".workflow"));
        }
        else if (string.IsNullOrWhiteSpace(Path.GetExtension(normalized)))
        {
            aliases.Add($"{normalized}.workflow");
        }

        return aliases.Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
