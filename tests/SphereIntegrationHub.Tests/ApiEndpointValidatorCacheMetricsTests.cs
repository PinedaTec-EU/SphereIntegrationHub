using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;
using SphereIntegrationHub.Tests.Helpers;

namespace SphereIntegrationHub.Tests;

/// <summary>
/// Tests that <see cref="ApiEndpointValidator"/> emits the expected cache metrics:
///   sih.cache.swagger.operations.hits
///   sih.cache.swagger.operations.misses
///   sih.cache.swagger.operations.size  (ObservableGauge)
///   sih.cache.swagger.load.duration    (Histogram, tagged with cache.hit)
/// </summary>
public sealed class ApiEndpointValidatorCacheMetricsTests : IDisposable
{
    private const string MeterName = "SphereIntegrationHub";
    private const string Hits = "sih.cache.swagger.operations.hits";
    private const string Misses = "sih.cache.swagger.operations.misses";
    private const string Size = "sih.cache.swagger.operations.size";
    private const string Duration = "sih.cache.swagger.load.duration";

    private readonly string _cacheRoot;
    private readonly string _swaggerPath;
    private readonly WorkflowDefinition _workflow;
    private readonly ApiCatalogVersion _catalog;

    public ApiEndpointValidatorCacheMetricsTests()
    {
        _cacheRoot = Path.Combine(Path.GetTempPath(), $"sih-swagger-metrics-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_cacheRoot);

        _swaggerPath = Path.Combine(_cacheRoot, "accounts.json");
        WriteSwagger(_swaggerPath);

        _workflow = new WorkflowDefinition
        {
            Version = "1.0",
            Id = "test-01",
            Name = "TestWorkflow",
            References = new WorkflowReference
            {
                Apis = [new ApiReferenceItem { Name = "accounts", Definition = "accounts" }]
            },
            Stages =
            [
                new WorkflowStageDefinition
                {
                    Name = "get-account",
                    Kind = WorkflowStageKind.Endpoint,
                    ApiRef = "accounts",
                    Endpoint = "/accounts/{id}",
                    HttpVerb = "GET"
                }
            ]
        };

        _catalog = new ApiCatalogVersion
        {
            Version = "v1",
            Definitions =
            [
                new ApiDefinition
                {
                    Name = "accounts",
                    SwaggerUrl = "accounts.json",
                    BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["test"] = "https://example.test" }
                }
            ]
        };
    }

    [Fact]
    public void Validate_FirstCall_RecordsMiss()
    {
        using var metrics = new MetricsCollector(MeterName);
        var validator = new ApiEndpointValidator();

        validator.Validate(_workflow, _catalog, _cacheRoot, validateRequiredParameters: false, verbose: false);

        Assert.Equal(1, metrics.Sum(Misses));
        Assert.Equal(0, metrics.Sum(Hits));
    }

    [Fact]
    public void Validate_SecondCallWithUnchangedFile_RecordsHit()
    {
        var validator = new ApiEndpointValidator();
        // Prime the static cache
        validator.Validate(_workflow, _catalog, _cacheRoot, validateRequiredParameters: false, verbose: false);

        using var metrics = new MetricsCollector(MeterName);
        // Second call — same path, file not modified
        validator.Validate(_workflow, _catalog, _cacheRoot, validateRequiredParameters: false, verbose: false);

        Assert.Equal(1, metrics.Sum(Hits));
        Assert.Equal(0, metrics.Sum(Misses));
    }

    [Fact]
    public void Validate_AfterFileModification_RecordsMissAgain()
    {
        var validator = new ApiEndpointValidator();
        // Prime the cache with original content
        validator.Validate(_workflow, _catalog, _cacheRoot, validateRequiredParameters: false, verbose: false);

        // Overwrite the swagger file (changes lastWriteTime)
        WriteSwagger(_swaggerPath);

        using var metrics = new MetricsCollector(MeterName);
        validator.Validate(_workflow, _catalog, _cacheRoot, validateRequiredParameters: false, verbose: false);

        Assert.Equal(1, metrics.Sum(Misses));
        Assert.Equal(0, metrics.Sum(Hits));
    }

    [Fact]
    public void Validate_Miss_RecordsDurationTaggedWithCacheHitFalse()
    {
        using var metrics = new MetricsCollector(MeterName);
        var validator = new ApiEndpointValidator();

        validator.Validate(_workflow, _catalog, _cacheRoot, validateRequiredParameters: false, verbose: false);

        var durations = metrics.GetMeasurements(Duration);
        Assert.NotEmpty(durations);
        var missEntry = durations.FirstOrDefault(d =>
            d.Tags.TryGetValue("cache.hit", out var v) && v is false);
        Assert.NotEqual(default, missEntry);
        Assert.True(missEntry.Value >= 0);
    }

    [Fact]
    public void Validate_Hit_RecordsDurationTaggedWithCacheHitTrue()
    {
        var validator = new ApiEndpointValidator();
        // Prime the cache
        validator.Validate(_workflow, _catalog, _cacheRoot, validateRequiredParameters: false, verbose: false);

        using var metrics = new MetricsCollector(MeterName);
        // Hit
        validator.Validate(_workflow, _catalog, _cacheRoot, validateRequiredParameters: false, verbose: false);

        var durations = metrics.GetMeasurements(Duration);
        Assert.NotEmpty(durations);
        var hitEntry = durations.FirstOrDefault(d =>
            d.Tags.TryGetValue("cache.hit", out var v) && v is true);
        Assert.NotEqual(default, hitEntry);
        Assert.True(hitEntry.Value >= 0);
    }

    [Fact]
    public void Validate_SizeGauge_IsAtLeastOneAfterCachingOneDefinition()
    {
        var validator = new ApiEndpointValidator();
        // Prime cache so the gauge has at least one entry
        validator.Validate(_workflow, _catalog, _cacheRoot, validateRequiredParameters: false, verbose: false);

        using var metrics = new MetricsCollector(MeterName);
        metrics.RecordObservableInstruments();

        var gaugeValues = metrics.GetValues(Size);
        Assert.NotEmpty(gaugeValues);
        // The static gauge reflects all definitions across all tests; at least one entry exists
        Assert.Contains(gaugeValues, v => v >= 1);
    }

    [Fact]
    public void Validate_DurationTaggedWithApiDefinitionName()
    {
        using var metrics = new MetricsCollector(MeterName);
        var validator = new ApiEndpointValidator();

        validator.Validate(_workflow, _catalog, _cacheRoot, validateRequiredParameters: false, verbose: false);

        var durations = metrics.GetMeasurements(Duration);
        Assert.NotEmpty(durations);
        Assert.Contains(durations, d =>
            d.Tags.TryGetValue("api.definition", out var v) &&
            string.Equals(v?.ToString(), "accounts", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        try { Directory.Delete(_cacheRoot, recursive: true); }
        catch { /* ignore cleanup failures */ }
    }

    private static void WriteSwagger(string path)
    {
        File.WriteAllText(path, """
        {
          "paths": {
            "/accounts/{accountId}": {
              "get": { "parameters": [] }
            }
          }
        }
        """);
    }
}
