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
            "--report-format", "both",
            "--capture-http", "bodies",
            "--refresh-cache",
            "--dry-run",
            "--verbose",
            "--debug",
            "--mocked",
            "--no-redact",
            "--no-summary"
        });

        Assert.Equal("a.workflow", result.WorkflowPath);
        Assert.Equal("dev", result.Environment);
        Assert.Equal("api.json", result.CatalogPath);
        Assert.Equal("env.txt", result.EnvFileOverride);
        Assert.Equal("vars.wfvars", result.VarsFilePath);
        Assert.Equal("both", result.ReportFormat);
        Assert.Equal("bodies", result.CaptureHttp);
        Assert.True(result.RefreshCache);
        Assert.True(result.DryRun);
        Assert.True(result.Verbose);
        Assert.True(result.Debug);
        Assert.False(result.Mocked);
        Assert.False(result.RedactSensitiveData);
        Assert.False(result.SummaryConsole);
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
            "--report-format", "json",
            "--capture-http", "headers",
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
        Assert.Equal("json", result.ReportFormat);
        Assert.Equal("headers", result.CaptureHttp);
        Assert.True(result.RefreshCache);
        Assert.False(result.DryRun);
        Assert.True(result.Verbose);
        Assert.True(result.Debug);
        Assert.True(result.Mocked);
    }

    [Fact]
    public void ParseArgs_Version_SetsShowVersion()
    {
        ICliArgumentParser parser = new CliArgumentParser();

        var result = parser.ParseArgs(new[] { "--version" });

        Assert.True(result.ShowVersion);
        Assert.Null(result.Error);
    }
}
