using SphereIntegrationHub.cli;

namespace SphereIntegrationHub.Tests;

public sealed class CliPathResolverTests
{
    [Fact]
    public void ResolveVarsFilePath_InvalidExtension_ReturnsError()
    {
        ICliPathResolver resolver = new CliPathResolver();

        var path = resolver.ResolveVarsFilePath("vars.txt", "/tmp/workflow.yaml", out _, out var error);

        Assert.Null(path);
        Assert.Equal("Vars file must use the .wfvars extension.", error);
    }

    [Fact]
    public void ResolveVarsFilePath_MissingFile_ReturnsError()
    {
        ICliPathResolver resolver = new CliPathResolver();
        var varsPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.wfvars");

        var path = resolver.ResolveVarsFilePath(varsPath, "/tmp/workflow.yaml", out _, out var error);

        Assert.Null(path);
        Assert.Contains("Vars file was not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveVarsFilePath_AutoDetectsFile()
    {
        ICliPathResolver resolver = new CliPathResolver();
        var tempRoot = Path.Combine(Path.GetTempPath(), $"aos-vars-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var workflowPath = Path.Combine(tempRoot, "order.workflow");
        var varsPath = Path.Combine(tempRoot, "order.wfvars");
        File.WriteAllText(varsPath, "key: value");

        var path = resolver.ResolveVarsFilePath(null, workflowPath, out var message, out var error);

        Assert.Null(error);
        Assert.Equal(varsPath, path);
        Assert.Contains("(auto)", message);
    }

    [Fact]
    public void ResolveDefaultCatalogPath_UsesParentDirectory()
    {
        ICliPathResolver resolver = new CliPathResolver();
        var tempRoot = Path.Combine(Path.GetTempPath(), $"aos-catalog-{Guid.NewGuid():N}");
        var workflows = Path.Combine(tempRoot, "workflows");
        Directory.CreateDirectory(workflows);
        var workflowPath = Path.Combine(workflows, "main.workflow");

        var catalogPath = resolver.ResolveDefaultCatalogPath(workflowPath);

        Assert.Equal(Path.Combine(tempRoot, "api-catalog.json"), catalogPath);
    }
}
