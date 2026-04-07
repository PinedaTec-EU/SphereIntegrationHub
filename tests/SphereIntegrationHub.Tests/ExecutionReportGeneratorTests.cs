using SphereIntegrationHub.cli;
using SphereIntegrationHub.Services;

namespace SphereIntegrationHub.Tests;

public sealed class ExecutionReportGeneratorTests
{
    [Fact]
    public async Task GenerateAndOpenAsync_WithDirectoryInput_BuildsPickerAndDefaultsToLatestUlid()
    {
        var root = Path.Combine(Path.GetTempPath(), $"sih-report-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var olderPath = Path.Combine(root, "sample.01KNGXHCBZWN6KMC3DZDZAJRTJ.workflow.report.json");
        var newerPath = Path.Combine(root, "sample.01KNGXHCZZZZ6KMC3DZDZAJRTJ.workflow.report.json");

        await File.WriteAllTextAsync(olderPath, CreateReportJson("01KNGXHCBZWN6KMC3DZDZAJRTJ", "Older Workflow"));
        await File.WriteAllTextAsync(newerPath, CreateReportJson("01KNGXHCZZZZ6KMC3DZDZAJRTJ", "Newer Workflow"));

        var output = new TestOutputProvider();
        var generator = new ExecutionReportGenerator(output);

        var result = await generator.GenerateAndOpenAsync(
            new InlineArguments(
                IsReportCommand: true,
                ExecutionReportPath: root,
                OpenAfterGenerate: false),
            CancellationToken.None);

        Assert.Equal(0, result);

        var htmlPath = Path.Combine(root, $"{Path.GetFileName(root)}.reports.workflow.report.html");
        Assert.True(File.Exists(htmlPath));

        var html = await File.ReadAllTextAsync(htmlPath);
        Assert.Contains("report-picker", html, StringComparison.Ordinal);
        Assert.Contains("Older Workflow", html, StringComparison.Ordinal);
        Assert.Contains("Newer Workflow", html, StringComparison.Ordinal);
        Assert.Contains("const _initialReportIndex = 1;", html, StringComparison.Ordinal);
        Assert.Contains("01KNGXHCZZZZ6KMC3DZDZAJRTJ", html, StringComparison.Ordinal);
        Assert.Contains("Sphere Integration Hub (SIH)", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Sphere Integration Hub icon\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAndOpenAsync_WithSkippedStage_RendersNotExecutedCopy()
    {
        var root = Path.Combine(Path.GetTempPath(), $"sih-report-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var reportPath = Path.Combine(root, "sample.01KNGXHCZZZZ6KMC3DZDZAJRTJ.workflow.report.json");
        await File.WriteAllTextAsync(reportPath, CreateSkippedReportJson());

        var output = new TestOutputProvider();
        var generator = new ExecutionReportGenerator(output);

        var result = await generator.GenerateAndOpenAsync(
            new InlineArguments(
                IsReportCommand: true,
                ExecutionReportPath: root,
                OpenAfterGenerate: false),
            CancellationToken.None);

        Assert.Equal(0, result);

        var htmlPath = Path.Combine(root, $"{Path.GetFileName(root)}.reports.workflow.report.html");
        var html = await File.ReadAllTextAsync(htmlPath);
        Assert.Contains("Not executed", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAndOpenAsync_WithWorkflowStages_KeepsDetailLogicSeparatedFromExpansion()
    {
        var root = Path.Combine(Path.GetTempPath(), $"sih-report-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var reportPath = Path.Combine(root, "sample.01KNGXHCZZZZ6KMC3DZDZAJRTJ.workflow.report.json");
        await File.WriteAllTextAsync(reportPath, CreateWorkflowTreeReportJson());

        var output = new TestOutputProvider();
        var generator = new ExecutionReportGenerator(output);

        var result = await generator.GenerateAndOpenAsync(
            new InlineArguments(
                IsReportCommand: true,
                ExecutionReportPath: root,
                OpenAfterGenerate: false),
            CancellationToken.None);

        Assert.Equal(0, result);

        var htmlPath = Path.Combine(root, $"{Path.GetFileName(root)}.reports.workflow.report.html");
        var html = await File.ReadAllTextAsync(htmlPath);
        Assert.Contains("if (_selected === idx && !hasChildren)", html, StringComparison.Ordinal);
        Assert.Contains("if (_selected === idx) {", html, StringComparison.Ordinal);
        Assert.Contains("class=\"tree-indent\"", html, StringComparison.Ordinal);
        Assert.Contains("--uri-indent:", html, StringComparison.Ordinal);
        Assert.Contains("return;", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAndOpenAsync_CentralizesTimelineDurationPlacement()
    {
        var root = Path.Combine(Path.GetTempPath(), $"sih-report-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var reportPath = Path.Combine(root, "sample.01KNGXHCZZZZ6KMC3DZDZAJRTJ.workflow.report.json");
        await File.WriteAllTextAsync(reportPath, CreateTimelineLabelReportJson());

        var output = new TestOutputProvider();
        var generator = new ExecutionReportGenerator(output);

        var result = await generator.GenerateAndOpenAsync(
            new InlineArguments(
                IsReportCommand: true,
                ExecutionReportPath: root,
                OpenAfterGenerate: false),
            CancellationToken.None);

        Assert.Equal(0, result);

        var htmlPath = Path.Combine(root, $"{Path.GetFileName(root)}.reports.workflow.report.html");
        var html = await File.ReadAllTextAsync(htmlPath);
        Assert.Contains("function syncTimelineLabels()", html, StringComparison.Ordinal);
        Assert.Contains("getDurationText(durationMs)", html, StringComparison.Ordinal);
        Assert.Contains("class=\"timeline-span\"", html, StringComparison.Ordinal);
        Assert.Contains("span-bar-duration", html, StringComparison.Ordinal);
        Assert.DoesNotContain("span-bar-label", html, StringComparison.Ordinal);
    }

    private static string CreateReportJson(string executionId, string workflowName)
    {
        var report = new WorkflowExecutionReport
        {
            ExecutionId = executionId,
            WorkflowName = workflowName,
            WorkflowId = "wf-1",
            WorkflowVersion = "1.0.0",
            WorkflowPath = "/tmp/test.workflow",
            Environment = "local",
            StartedAtUtc = DateTimeOffset.Parse("2026-04-06T10:00:00Z"),
            FinishedAtUtc = DateTimeOffset.Parse("2026-04-06T10:00:02Z"),
            DurationMs = 2000,
            Result = "Ok",
            Output = new Dictionary<string, object?>()
        };

        report.Metrics.TotalStages = 1;
        report.Metrics.ExecutedStages = 1;
        report.Stages.Add(new WorkflowStageExecutionRecord
        {
            WorkflowName = workflowName,
            StageName = "stage-1",
            StageKind = "Endpoint",
            StartedAtUtc = DateTimeOffset.Parse("2026-04-06T10:00:00Z"),
            FinishedAtUtc = DateTimeOffset.Parse("2026-04-06T10:00:01Z"),
            DurationMs = 1000,
            Status = "Ok"
        });

        return System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private static string CreateSkippedReportJson()
    {
        var report = new WorkflowExecutionReport
        {
            ExecutionId = "01KNGXHCZZZZ6KMC3DZDZAJRTJ",
            WorkflowName = "Skipped Workflow",
            WorkflowId = "wf-skip",
            WorkflowVersion = "1.0.0",
            WorkflowPath = "/tmp/skipped.workflow",
            Environment = "local",
            StartedAtUtc = DateTimeOffset.Parse("2026-04-06T10:00:00Z"),
            FinishedAtUtc = DateTimeOffset.Parse("2026-04-06T10:00:02Z"),
            DurationMs = 2000,
            Result = "Ok",
            Output = new Dictionary<string, object?>()
        };

        report.Metrics.TotalStages = 1;
        report.Metrics.SkippedStages = 1;
        report.Stages.Add(new WorkflowStageExecutionRecord
        {
            WorkflowName = "Skipped Workflow",
            StageName = "not-run-stage",
            StageKind = "Endpoint",
            StartedAtUtc = DateTimeOffset.Parse("2026-04-06T10:00:00Z"),
            FinishedAtUtc = DateTimeOffset.Parse("2026-04-06T10:00:00Z"),
            DurationMs = 0,
            Status = "Skipped",
            RunIf = "{{input.enabled}} == true"
        });

        return System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private static string CreateWorkflowTreeReportJson()
    {
        var report = new WorkflowExecutionReport
        {
            ExecutionId = "01KNGXHCZZZZ6KMC3DZDZAJRTJ",
            WorkflowName = "Workflow Tree",
            WorkflowId = "wf-tree",
            WorkflowVersion = "1.0.0",
            WorkflowPath = "/tmp/tree.workflow",
            Environment = "local",
            StartedAtUtc = DateTimeOffset.Parse("2026-04-06T10:00:00Z"),
            FinishedAtUtc = DateTimeOffset.Parse("2026-04-06T10:00:03Z"),
            DurationMs = 3000,
            Result = "Ok",
            Output = new Dictionary<string, object?>()
        };

        report.Metrics.TotalStages = 2;
        report.Metrics.ExecutedStages = 2;
        report.Stages.Add(new WorkflowStageExecutionRecord
        {
            WorkflowName = "Workflow Tree",
            StageName = "child-workflow",
            StageKind = "Workflow",
            StartedAtUtc = DateTimeOffset.Parse("2026-04-06T10:00:00Z"),
            FinishedAtUtc = DateTimeOffset.Parse("2026-04-06T10:00:02Z"),
            DurationMs = 2000,
            Status = "Ok",
            Depth = 0
        });
        report.Stages.Add(new WorkflowStageExecutionRecord
        {
            WorkflowName = "Nested Workflow",
            StageName = "inner-stage",
            StageKind = "Endpoint",
            StartedAtUtc = DateTimeOffset.Parse("2026-04-06T10:00:01Z"),
            FinishedAtUtc = DateTimeOffset.Parse("2026-04-06T10:00:02Z"),
            DurationMs = 1000,
            Status = "Ok",
            Depth = 1
        });

        return System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private static string CreateTimelineLabelReportJson()
    {
        var report = new WorkflowExecutionReport
        {
            ExecutionId = "01KNGXHCZZZZ6KMC3DZDZAJRTJ",
            WorkflowName = "Timeline Labels",
            WorkflowId = "wf-labels",
            WorkflowVersion = "1.0.0",
            WorkflowPath = "/tmp/labels.workflow",
            Environment = "local",
            StartedAtUtc = DateTimeOffset.Parse("2026-04-06T10:00:00Z"),
            FinishedAtUtc = DateTimeOffset.Parse("2026-04-06T10:00:03Z"),
            DurationMs = 3000,
            Result = "Ok",
            Output = new Dictionary<string, object?>()
        };

        report.Metrics.TotalStages = 2;
        report.Metrics.ExecutedStages = 2;
        report.Stages.Add(new WorkflowStageExecutionRecord
        {
            WorkflowName = "Timeline Labels",
            StageName = "short-stage",
            StageKind = "Endpoint",
            StartedAtUtc = DateTimeOffset.Parse("2026-04-06T10:00:00Z"),
            FinishedAtUtc = DateTimeOffset.Parse("2026-04-06T10:00:00.020Z"),
            DurationMs = 20,
            Status = "Ok"
        });
        report.Stages.Add(new WorkflowStageExecutionRecord
        {
            WorkflowName = "Timeline Labels",
            StageName = "long-stage",
            StageKind = "Workflow",
            StartedAtUtc = DateTimeOffset.Parse("2026-04-06T10:00:01Z"),
            FinishedAtUtc = DateTimeOffset.Parse("2026-04-06T10:00:02.500Z"),
            DurationMs = 1500,
            Status = "Ok"
        });

        return System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private sealed class TestOutputProvider : ICliOutputProvider
    {
        public TextWriter Out { get; } = new StringWriter();
        public TextWriter Error { get; } = new StringWriter();
        public bool UseColors => false;
    }
}
