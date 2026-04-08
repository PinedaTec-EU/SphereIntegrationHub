using SphereIntegrationHub.Services;
using SphereIntegrationHub.Tests.Helpers;

namespace SphereIntegrationHub.Tests;

[Collection(CliCacheMetricsCollection.Name)]
/// <summary>
/// Tests that <see cref="WorkflowLoader"/> emits the expected cache metrics:
///   sih.cache.workflow.document.hits
///   sih.cache.workflow.document.misses
///   sih.cache.workflow.document.size  (ObservableGauge, per-instance)
///   sih.cache.workflow.load.duration  (Histogram, tagged with cache.hit)
/// </summary>
public sealed class WorkflowLoaderCacheMetricsTests : IDisposable
{
    private const string MeterName = "SphereIntegrationHub";
    private const string Hits = "sih.cache.workflow.document.hits";
    private const string Misses = "sih.cache.workflow.document.misses";
    private const string Size = "sih.cache.workflow.document.size";
    private const string Duration = "sih.cache.workflow.load.duration";

    private readonly string _workflowPath;
    private readonly string _tempDir;

    public WorkflowLoaderCacheMetricsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sih-loader-metrics-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _workflowPath = Path.Combine(_tempDir, "test.workflow");
        WriteWorkflow(_workflowPath, "TestWorkflow");
    }

    [Fact]
    public void Load_FirstCall_RecordsMiss()
    {
        using var metrics = new MetricsCollector(MeterName);
        var loader = new WorkflowLoader();

        loader.Load(_workflowPath);

        Assert.Contains(GetMeasurementsForWorkflow(metrics, Misses, _workflowPath), measurement => measurement.Value == 1);
        Assert.Empty(GetMeasurementsForWorkflow(metrics, Hits, _workflowPath));
    }

    [Fact]
    public void Load_SecondCallWithUnchangedFile_RecordsHit()
    {
        var loader = new WorkflowLoader();
        // Prime the cache
        loader.Load(_workflowPath);

        using var metrics = new MetricsCollector(MeterName);
        loader.Load(_workflowPath);

        Assert.Contains(GetMeasurementsForWorkflow(metrics, Hits, _workflowPath), measurement => measurement.Value == 1);
        Assert.Empty(GetMeasurementsForWorkflow(metrics, Misses, _workflowPath));
    }

    [Fact]
    public void Load_AfterFileModification_RecordsMissAgain()
    {
        var loader = new WorkflowLoader();
        // Prime the cache
        loader.Load(_workflowPath);

        // Ensure the filesystem timestamp changes before rewriting the file.
        Thread.Sleep(1100);
        WriteWorkflow(_workflowPath, "ModifiedWorkflow");

        using var metrics = new MetricsCollector(MeterName);
        loader.Load(_workflowPath);

        Assert.Contains(GetMeasurementsForWorkflow(metrics, Misses, _workflowPath), measurement => measurement.Value == 1);
        Assert.Empty(GetMeasurementsForWorkflow(metrics, Hits, _workflowPath));
    }

    [Fact]
    public void Load_WithEnvFileOverride_NeverCaches()
    {
        // A real env override file avoids resolve errors and forces the loader down
        // the "never cache" path on every load.
        var envFile = Path.Combine(_tempDir, ".env");
        File.WriteAllText(envFile, string.Empty);

        var loader = new WorkflowLoader();

        using var metrics = new MetricsCollector(MeterName);
        var first = loader.Load(_workflowPath, envFileOverride: envFile);
        var second = loader.Load(_workflowPath, envFileOverride: envFile);
        metrics.RecordObservableInstruments();

        Assert.Equal(2, GetMeasurementsForWorkflow(metrics, Misses, _workflowPath).Count);
        Assert.Empty(GetMeasurementsForWorkflow(metrics, Hits, _workflowPath));
        Assert.NotSame(first, second);
        Assert.Contains(metrics.GetValues(Size), value => value == 0);
    }

    [Fact]
    public void Load_WorkflowWithReferencedEnvFile_NeverCaches()
    {
        // Workflow that has references.environmentFile should bypass cache
        // so changes to the env file are always picked up.
        var envFile = Path.Combine(_tempDir, "workflow.env");
        File.WriteAllText(envFile, "SOME_VAR=value");

        var wfWithEnvPath = Path.Combine(_tempDir, "wf-with-env.workflow");
        File.WriteAllText(wfWithEnvPath, $"""
            version: "1.0"
            id: "env-wf"
            name: "EnvWorkflow"
            references:
              environmentFile: {envFile}
            """);

        var loader = new WorkflowLoader();
        using var metrics = new MetricsCollector(MeterName);

        var first = loader.Load(wfWithEnvPath);
        var second = loader.Load(wfWithEnvPath);
        metrics.RecordObservableInstruments();

        // Both loads must be misses — env-referencing workflows are never stored in cache
        Assert.Equal(2, GetMeasurementsForWorkflow(metrics, Misses, wfWithEnvPath).Count);
        Assert.Empty(GetMeasurementsForWorkflow(metrics, Hits, wfWithEnvPath));
        Assert.NotSame(first, second);
        Assert.Contains(metrics.GetValues(Size), value => value == 0);
    }

    [Fact]
    public void Load_Miss_RecordsDurationTaggedWithCacheHitFalse()
    {
        using var metrics = new MetricsCollector(MeterName);
        var loader = new WorkflowLoader();

        loader.Load(_workflowPath);

        var durations = metrics.GetMeasurements(Duration);
        Assert.NotEmpty(durations);
        Assert.Contains(durations, d =>
            d.Tags.TryGetValue("workflow.path", out var path) &&
            string.Equals(path?.ToString(), Path.GetFullPath(_workflowPath), StringComparison.OrdinalIgnoreCase) &&
            d.Tags.TryGetValue("cache.hit", out var v) && v is false);
    }

    [Fact]
    public void Load_Hit_RecordsDurationTaggedWithCacheHitTrue()
    {
        var loader = new WorkflowLoader();
        // Prime the cache
        loader.Load(_workflowPath);

        using var metrics = new MetricsCollector(MeterName);
        loader.Load(_workflowPath);

        var durations = metrics.GetMeasurements(Duration);
        Assert.NotEmpty(durations);
        Assert.Contains(durations, d =>
            d.Tags.TryGetValue("workflow.path", out var path) &&
            string.Equals(path?.ToString(), Path.GetFullPath(_workflowPath), StringComparison.OrdinalIgnoreCase) &&
            d.Tags.TryGetValue("cache.hit", out var v) && v is true);
    }

    [Fact]
    public void Load_SizeGauge_ReportsOneAfterCachingOneDocument()
    {
        var loader = new WorkflowLoader();
        using var metrics = new MetricsCollector(MeterName);

        loader.Load(_workflowPath);
        metrics.RecordObservableInstruments();

        // The gauge is per-instance; at least one measurement of value 1 must exist
        // (previous test instances' gauges will report 0 as their caches are empty).
        var gaugeValues = metrics.GetValues(Size);
        Assert.NotEmpty(gaugeValues);
        Assert.Contains(gaugeValues, v => v == 1);
    }

    [Fact]
    public void Load_SizeGauge_IncreasesWithEachNewDocument()
    {
        var secondPath = Path.Combine(_tempDir, "second.workflow");
        WriteWorkflow(secondPath, "SecondWorkflow");

        var loader = new WorkflowLoader();
        using var metrics = new MetricsCollector(MeterName);

        loader.Load(_workflowPath);
        loader.Load(secondPath);

        metrics.RecordObservableInstruments();

        var gaugeValues = metrics.GetValues(Size);
        Assert.Contains(gaugeValues, v => v == 2);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* ignore cleanup failures */ }
    }

    private static void WriteWorkflow(string path, string name)
    {
        File.WriteAllText(path, $"""
            version: "1.0"
            id: "{name.ToLowerInvariant()}-01"
            name: "{name}"
            """);
    }

    private static IReadOnlyList<(double Value, IReadOnlyDictionary<string, object?> Tags)> GetMeasurementsForWorkflow(
        MetricsCollector metrics,
        string instrumentName,
        string workflowPath)
    {
        var fullPath = Path.GetFullPath(workflowPath);
        return metrics
            .GetMeasurements(instrumentName)
            .Where(measurement =>
                measurement.Tags.TryGetValue("workflow.path", out var path) &&
                string.Equals(path?.ToString(), fullPath, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
