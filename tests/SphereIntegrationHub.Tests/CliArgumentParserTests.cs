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
            "--no-summary",
            "--assertion-failures-block", "false"
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
        Assert.False(result.AssertionFailuresBlock);
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

    [Fact]
    public void ParseArgs_InvalidAssertionFailuresBlock_ReturnsError()
    {
        ICliArgumentParser parser = new CliArgumentParser();

        var result = parser.ParseArgs(new[] { "--assertion-failures-block", "maybe" });

        Assert.Equal("Invalid value for --assertion-failures-block. Use true or false.", result.Error);
    }

    [Fact]
    public void ParseArgs_SnapshotCreate_ParsesValues()
    {
        ICliArgumentParser parser = new CliArgumentParser();

        var result = parser.ParseArgs(new[]
        {
            "snapshot",
            "create",
            "report.json",
            "--output",
            "baseline.json",
            "--name",
            "happy-path"
        });

        Assert.True(result.IsSnapshotCommand);
        Assert.Equal("create", result.SnapshotAction);
        Assert.Equal("report.json", result.ExecutionReportPath);
        Assert.Equal("baseline.json", result.ReportOutputPath);
        Assert.Equal("happy-path", result.SnapshotName);
    }

    [Fact]
    public void ParseArgs_SnapshotCompare_RequiresSnapshotPath()
    {
        ICliArgumentParser parser = new CliArgumentParser();

        var result = parser.ParseArgs(new[] { "snapshot", "compare", "report.json" });

        Assert.Equal("Missing snapshot path. Usage: sih snapshot compare <report-json> --snapshot <snapshot-json>", result.Error);
    }

    [Fact]
    public void ParseArgs_ReportCommand_ParsesSnapshotPath()
    {
        ICliArgumentParser parser = new CliArgumentParser();

        var result = parser.ParseArgs(new[] { "report", "output", "--catalog", "api.catalog", "--snapshot", "snapshots", "--no-open" });

        Assert.True(result.IsReportCommand);
        Assert.Equal("output", result.ExecutionReportPath);
        Assert.Equal("api.catalog", result.CatalogPath);
        Assert.Equal("snapshots", result.SnapshotPath);
        Assert.False(result.OpenAfterGenerate);
    }
}
