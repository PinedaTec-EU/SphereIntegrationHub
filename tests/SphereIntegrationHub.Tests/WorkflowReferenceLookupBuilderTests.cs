using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;

namespace SphereIntegrationHub.Tests;

public sealed class WorkflowReferenceLookupBuilderTests
{
    [Fact]
    public void BuildWorkflowReferenceLookup_ResolvesPathsAndTracksErrors()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "workflow-tests");
        var workflowPath = Path.Combine(baseDir, "root.workflow");
        var references = new List<WorkflowReferenceItem>
        {
            new() { Name = "child", Path = "child.workflow" },
            new() { Name = "", Path = "missing.workflow" },
            new() { Name = "child", Path = "duplicate.workflow" }
        };
        var errors = new List<string>();

        var lookup = WorkflowReferenceLookupBuilder.BuildWorkflowReferenceLookup(references, workflowPath, errors);

        Assert.Contains(errors, error => error.Contains("Reference name is required", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("Duplicate reference name", StringComparison.OrdinalIgnoreCase));
        Assert.True(lookup.TryGetValue("child", out var resolvedPath));
        Assert.Equal(Path.GetFullPath(Path.Combine(baseDir, "child.workflow")), resolvedPath);
    }

    [Fact]
    public void BuildApiReferenceLookup_FlagsMissingDefinitionAndDuplicates()
    {
        var references = new List<ApiReferenceItem>
        {
            new() { Name = "accounts", Definition = "" },
            new() { Name = "accounts", Definition = "acct" },
            new() { Name = "accounts", Definition = "acct2" }
        };
        var errors = new List<string>();

        var lookup = WorkflowReferenceLookupBuilder.BuildApiReferenceLookup(references, errors);

        Assert.Contains(errors, error => error.Contains("definition is required", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("Duplicate API reference name", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("definition is required", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("Duplicate API reference name", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("accounts", lookup);
    }
}
