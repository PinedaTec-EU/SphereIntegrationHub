using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.Services;

internal static class WorkflowReferencePathResolver
{
    private static readonly IReadOnlyDictionary<string, string> EmptyStrings =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> EmptyNestedStrings =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
    private static readonly TemplateResolver TemplateResolver = new();

    public static Dictionary<string, string> BuildLookup(
        IReadOnlyList<WorkflowReferenceItem>? references,
        string workflowPath,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        IReadOnlyDictionary<string, string>? inputs = null,
        IReadOnlyDictionary<string, string>? globals = null,
        IReadOnlyDictionary<string, string>? context = null,
        List<string>? errors = null)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (references is null)
        {
            return lookup;
        }

        var templateContext = BuildTemplateContext(workflowPath, environmentVariables, inputs, globals, context);
        foreach (var reference in references)
        {
            if (string.IsNullOrWhiteSpace(reference.Name))
            {
                errors?.Add("Reference name is required.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(reference.Path))
            {
                errors?.Add($"Reference '{reference.Name}' path is required.");
                continue;
            }

            if (!lookup.TryAdd(reference.Name, string.Empty))
            {
                errors?.Add($"Duplicate reference name '{reference.Name}'.");
                continue;
            }

            try
            {
                lookup[reference.Name] = ResolvePath(reference.Path, workflowPath, templateContext);
            }
            catch (Exception ex)
            {
                lookup.Remove(reference.Name);
                if (errors is null)
                {
                    throw;
                }

                errors.Add($"Reference '{reference.Name}' path '{reference.Path}' could not be resolved: {ex.Message}");
            }
        }

        return lookup;
    }

    public static string ResolvePath(
        string referencePath,
        string workflowPath,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        IReadOnlyDictionary<string, string>? inputs = null,
        IReadOnlyDictionary<string, string>? globals = null,
        IReadOnlyDictionary<string, string>? context = null)
    {
        var templateContext = BuildTemplateContext(workflowPath, environmentVariables, inputs, globals, context);
        return ResolvePath(referencePath, workflowPath, templateContext);
    }

    public static string ResolvePath(string referencePath, TemplateContext templateContext)
    {
        if (string.IsNullOrWhiteSpace(templateContext.WorkflowPath))
        {
            throw new InvalidOperationException("Workflow path is required to resolve file paths.");
        }

        return ResolvePath(referencePath, templateContext.WorkflowPath, templateContext);
    }

    private static string ResolvePath(string referencePath, string workflowPath, TemplateContext templateContext)
    {
        string resolvedPath;
        try
        {
            resolvedPath = referencePath.Contains("{{", StringComparison.Ordinal)
                ? TemplateResolver.ResolveTemplate(referencePath, templateContext)
                : referencePath;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Path '{referencePath}' could not be resolved: {ex.Message}", ex);
        }

        if (string.IsNullOrWhiteSpace(resolvedPath))
        {
            throw new InvalidOperationException($"Path '{referencePath}' resolved to an empty value.");
        }

        return Path.IsPathRooted(resolvedPath)
            ? Path.GetFullPath(resolvedPath)
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(workflowPath) ?? string.Empty, resolvedPath));
    }

    private static TemplateContext BuildTemplateContext(
        string workflowPath,
        IReadOnlyDictionary<string, string>? environmentVariables,
        IReadOnlyDictionary<string, string>? inputs,
        IReadOnlyDictionary<string, string>? globals,
        IReadOnlyDictionary<string, string>? context)
    {
        return new TemplateContext(
            inputs ?? EmptyStrings,
            globals ?? EmptyStrings,
            context ?? EmptyStrings,
            EmptyNestedStrings,
            EmptyNestedStrings,
            EmptyNestedStrings,
            environmentVariables ?? EmptyStrings,
            InputJson: null,
            GlobalJson: null,
            ContextJson: null,
            EndpointOutputJson: null,
            WorkflowOutputJson: null,
            WorkflowPath: workflowPath);
    }
}
