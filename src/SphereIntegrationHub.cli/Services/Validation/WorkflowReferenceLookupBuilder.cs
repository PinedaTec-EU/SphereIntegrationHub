using SphereIntegrationHub.Definitions;
using System;
using System.Collections.Generic;
using System.IO;

namespace SphereIntegrationHub.Services;

internal static class WorkflowReferenceLookupBuilder
{
    public static Dictionary<string, string> BuildWorkflowReferenceLookup(
        IReadOnlyList<WorkflowReferenceItem>? references,
        string workflowPath,
        List<string> errors)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (references is null)
        {
            return lookup;
        }

        var baseDirectory = Path.GetDirectoryName(workflowPath) ?? string.Empty;
        foreach (var reference in references)
        {
            if (string.IsNullOrWhiteSpace(reference.Name))
            {
                errors.Add("Reference name is required.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(reference.Path))
            {
                errors.Add($"Reference '{reference.Name}' path is required.");
                continue;
            }

            if (lookup.ContainsKey(reference.Name))
            {
                errors.Add($"Duplicate reference name '{reference.Name}'.");
                continue;
            }

            var resolvedPath = Path.GetFullPath(Path.Combine(baseDirectory, reference.Path));
            lookup.Add(reference.Name, resolvedPath);
        }

        return lookup;
    }

    public static HashSet<string> BuildApiReferenceLookup(
        IReadOnlyList<ApiReferenceItem>? references,
        List<string> errors)
    {
        var lookup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (references is null)
        {
            return lookup;
        }

        foreach (var reference in references)
        {
            if (string.IsNullOrWhiteSpace(reference.Name))
            {
                errors.Add("API reference name is required.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(reference.Definition))
            {
                errors.Add($"API reference '{reference.Name}' definition is required.");
                continue;
            }

            if (!lookup.Add(reference.Name))
            {
                errors.Add($"Duplicate API reference name '{reference.Name}'.");
            }
        }

        return lookup;
    }
}
