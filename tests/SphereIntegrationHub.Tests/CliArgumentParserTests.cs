using SphereIntegrationHub.cli;

namespace SphereIntegrationHub.Tests;

public sealed class CliArgumentParserTests
{
    [Fact]
    public void ParseArgs_UnknownArgument_ReturnsError()
    {
        ICliArgumentParser parser = new CliArgumentParser();

        var result = parser.ParseArgs(new[] { "--nope" });

        Assert.NotNull(result.Error);
    }

    [Fact]
    public void ParseArgs_MissingValue_ReturnsError()
    {
        ICliArgumentParser parser = new CliArgumentParser();

        var result = parser.ParseArgs(new[] { "--workflow" });

        Assert.Equal("Missing value for --workflow.", result.Error);
    }

    [Fact]
    public void ParseArgs_DryRun_ParsesFlagsAndValues()
    {
        ICliArgumentParser parser = new CliArgumentParser();

        var result = parser.ParseArgs(new[]
        {
            "--workflow", "a.workflow",
            "--env", "dev",
            "--catalog", "api.json",
            "--envfile", "env.txt",
            "--varsfile", "vars.wfvars",
            "--refresh-cache",
            "--dry-run",
            "--verbose",
            "--debug",
            "--mocked"
        });

        Assert.Equal("a.workflow", result.WorkflowPath);
        Assert.Equal("dev", result.Environment);
        Assert.Equal("api.json", result.CatalogPath);
        Assert.Equal("env.txt", result.EnvFileOverride);
        Assert.Equal("vars.wfvars", result.VarsFilePath);
        Assert.True(result.RefreshCache);
        Assert.True(result.DryRun);
        Assert.True(result.Verbose);
        Assert.True(result.Debug);
        Assert.False(result.Mocked);
    }

    [Fact]
    public void ParseArgs_NoDryRun_ParsesFlagsAndValues()
    {
        ICliArgumentParser parser = new CliArgumentParser();

        var result = parser.ParseArgs(new[]
        {
            "--workflow", "a.workflow",
            "--env", "dev",
            "--catalog", "api.json",
            "--envfile", "env.txt",
            "--varsfile", "vars.wfvars",
            "--refresh-cache",
            "--verbose",
            "--debug",
            "--mocked"
        });

        Assert.Equal("a.workflow", result.WorkflowPath);
        Assert.Equal("dev", result.Environment);
        Assert.Equal("api.json", result.CatalogPath);
        Assert.Equal("env.txt", result.EnvFileOverride);
        Assert.Equal("vars.wfvars", result.VarsFilePath);
        Assert.True(result.RefreshCache);
        Assert.False(result.DryRun);
        Assert.True(result.Verbose);
        Assert.True(result.Debug);
        Assert.True(result.Mocked);
    }
}
