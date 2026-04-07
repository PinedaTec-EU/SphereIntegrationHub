using FluentAssertions;
using SphereIntegrationHub.MCP.Services.Integration;
using SphereIntegrationHub.MCP.Services.Validation;
using SphereIntegrationHub.MCP.Tests.TestHelpers;
using Xunit;

namespace SphereIntegrationHub.MCP.Tests.Unit;

/// <summary>
/// Tests that <see cref="WorkflowValidatorService"/> emits the expected cache metrics:
///   sih.cache.workflow.validation.hits
///   sih.cache.workflow.validation.misses
///   sih.cache.workflow.validation.evictions
///   sih.cache.workflow.validation.size     (UpDownCounter)
///   sih.cache.workflow.validation.duration (Histogram, tagged with cache.hit)
/// </summary>
public sealed class ValidationCacheMetricsTests : IDisposable
{
    private const string MeterName = "SphereIntegrationHub.MCP";
    private const string Hits = "sih.cache.workflow.validation.hits";
    private const string Misses = "sih.cache.workflow.validation.misses";
    private const string Evictions = "sih.cache.workflow.validation.evictions";
    private const string SizeInstrument = "sih.cache.workflow.validation.size";
    private const string Duration = "sih.cache.workflow.validation.duration";

    private readonly MockFileSystem _mockFs;
    private readonly WorkflowValidatorService _service;
    private readonly string _workflowPath;

    public ValidationCacheMetricsTests()
    {
        _mockFs = new MockFileSystem();
        _service = new WorkflowValidatorService(new SihServicesAdapter(_mockFs.RootPath));

        _mockFs.AddWorkflow("test.workflow", MinimalWorkflowYaml("TestWorkflow", "test-01"));
        _workflowPath = Path.Combine(_mockFs.WorkflowsPath, "test.workflow");
    }

    [Fact]
    public async Task ValidateWorkflowAsync_FirstCall_RecordsMiss()
    {
        using var metrics = new MetricsCollector(MeterName);

        await _service.ValidateWorkflowAsync(_workflowPath);

        metrics.Sum(Misses).Should().Be(1);
        metrics.Sum(Hits).Should().Be(0);
    }

    [Fact]
    public async Task ValidateWorkflowAsync_SecondCallWithSameContent_RecordsHit()
    {
        // Prime the cache
        await _service.ValidateWorkflowAsync(_workflowPath);

        using var metrics = new MetricsCollector(MeterName);
        await _service.ValidateWorkflowAsync(_workflowPath);

        metrics.Sum(Hits).Should().Be(1);
        metrics.Sum(Misses).Should().Be(0);
    }

    [Fact]
    public async Task ValidateWorkflowAsync_DifferentContent_RecordsSeparateMisses()
    {
        var secondPath = Path.Combine(_mockFs.WorkflowsPath, "second.workflow");
        File.WriteAllText(secondPath, MinimalWorkflowYaml("SecondWorkflow", "second-01"));

        using var metrics = new MetricsCollector(MeterName);

        await _service.ValidateWorkflowAsync(_workflowPath);
        await _service.ValidateWorkflowAsync(secondPath);

        metrics.Sum(Misses).Should().Be(2);
        metrics.Sum(Hits).Should().Be(0);
    }

    [Fact]
    public async Task ValidateWorkflowAsync_AfterSameContentModifiedAndRestoredToOriginal_RecordsHit()
    {
        // First validation caches the original hash
        await _service.ValidateWorkflowAsync(_workflowPath);

        // Overwrite with different content, then restore original
        var original = await File.ReadAllTextAsync(_workflowPath);
        File.WriteAllText(_workflowPath, MinimalWorkflowYaml("Different", "diff-01"));
        await _service.ValidateWorkflowAsync(_workflowPath); // miss for new content
        File.WriteAllText(_workflowPath, original);

        using var metrics = new MetricsCollector(MeterName);
        await _service.ValidateWorkflowAsync(_workflowPath); // hit — same hash as first call

        metrics.Sum(Hits).Should().Be(1);
        metrics.Sum(Misses).Should().Be(0);
    }

    [Fact]
    public async Task ValidateWorkflowAsync_Miss_RecordsDurationTaggedCacheHitFalse()
    {
        using var metrics = new MetricsCollector(MeterName);

        await _service.ValidateWorkflowAsync(_workflowPath);

        var durations = metrics.GetMeasurements(Duration);
        durations.Should().NotBeEmpty();
        durations.Should().Contain(d =>
            d.Tags.ContainsKey("cache.hit") && Equals(d.Tags["cache.hit"], false));
    }

    [Fact]
    public async Task ValidateWorkflowAsync_Hit_RecordsDurationTaggedCacheHitTrue()
    {
        await _service.ValidateWorkflowAsync(_workflowPath); // prime

        using var metrics = new MetricsCollector(MeterName);
        await _service.ValidateWorkflowAsync(_workflowPath);

        var durations = metrics.GetMeasurements(Duration);
        durations.Should().NotBeEmpty();
        durations.Should().Contain(d =>
            d.Tags.ContainsKey("cache.hit") && Equals(d.Tags["cache.hit"], true));
    }

    [Fact]
    public async Task ValidateWorkflowAsync_Miss_IncreasesSizeByOne()
    {
        using var metrics = new MetricsCollector(MeterName);

        await _service.ValidateWorkflowAsync(_workflowPath);

        metrics.Sum(SizeInstrument).Should().Be(1);
    }

    [Fact]
    public async Task ValidateWorkflowAsync_Hit_DoesNotChangeSizeCounter()
    {
        await _service.ValidateWorkflowAsync(_workflowPath); // miss, size → 1

        using var metrics = new MetricsCollector(MeterName);
        await _service.ValidateWorkflowAsync(_workflowPath); // hit, no size change

        metrics.Sum(SizeInstrument).Should().Be(0);
    }

    [Fact]
    public async Task ValidateWorkflowAsync_WhenCacheIsFull_RecordsEvictionAndSizeDecrements()
    {
        // MaxValidationCacheEntries = 50 (internal constant in WorkflowValidatorService).
        // Fill a fresh service instance to capacity, then add one more entry.
        var freshService = new WorkflowValidatorService(new SihServicesAdapter(_mockFs.RootPath));
        var workflowDir = Path.Combine(_mockFs.RootPath, "eviction-wf");
        Directory.CreateDirectory(workflowDir);

        // Fill 50 entries (each file has unique content → unique hash → unique cache entry)
        for (var i = 0; i < 50; i++)
        {
            var p = Path.Combine(workflowDir, $"wf{i:D3}.workflow");
            File.WriteAllText(p, MinimalWorkflowYaml($"Workflow{i:D3}", $"wf-{i:D3}"));
            await freshService.ValidateWorkflowAsync(p);
        }

        using var metrics = new MetricsCollector(MeterName);

        // The 51st entry triggers an eviction
        var extraPath = Path.Combine(workflowDir, "wf-extra.workflow");
        File.WriteAllText(extraPath, MinimalWorkflowYaml("WorkflowExtra", "wf-extra"));
        await freshService.ValidateWorkflowAsync(extraPath);

        metrics.Sum(Evictions).Should().Be(1);
        // Assert the −1 decrement was recorded, without asserting the net sum:
        // another test running in parallel may emit +1 to the same static UpDownCounter,
        // making a Sum == 0 assertion flaky. Checking for the −1 value is robust.
        metrics.GetValues(SizeInstrument).Should().Contain(-1.0);
    }

    public void Dispose() => _mockFs.Dispose();

    private static string MinimalWorkflowYaml(string name, string id) => $"""
        version: "1.0"
        id: "{id}"
        name: "{name}"
        """;
}
