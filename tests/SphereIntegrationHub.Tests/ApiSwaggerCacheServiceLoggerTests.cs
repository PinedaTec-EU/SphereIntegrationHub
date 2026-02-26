using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;
using SphereIntegrationHub.Services.Interfaces;

namespace SphereIntegrationHub.Tests;

public sealed class ApiSwaggerCacheServiceLoggerTests
{
    [Fact]
    public async Task CacheSwaggerAsync_UsesLoggerWhenVerbose()
    {
        var logger = new TestLogger();
        var service = new ApiSwaggerCacheService(new HttpClient(), logger);
        var tempRoot = Path.Combine(Path.GetTempPath(), $"aos-swagger-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var sourcePath = Path.Combine(tempRoot, "accounts.json");
        await File.WriteAllTextAsync(sourcePath, "{\"paths\":{}}");

        var workflow = new WorkflowDefinition
        {
            References = new WorkflowReference
            {
                Apis = new List<ApiReferenceItem>
                {
                    new() { Name = "accounts", Definition = "accounts" }
                }
            }
        };

        var catalog = new ApiCatalogVersion
        {
            Version = "v1",
            BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["test"] = "https://example.test"
            },
            Definitions = new List<ApiDefinition>
            {
                new ApiDefinition { Name = "accounts", SwaggerUrl = new Uri(sourcePath).AbsoluteUri }
            }
        };

        var cacheRoot = Path.Combine(tempRoot, "cache");

        await service.CacheSwaggerAsync(catalog, workflow, "test", cacheRoot, refresh: true, verbose: true, CancellationToken.None);

        Assert.Contains(logger.Messages, message => message.Contains("Swagger cached", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class TestLogger : IExecutionLogger
    {
        public List<string> Messages { get; } = new();

        public void Info(string message) => Messages.Add(message);

        public void Error(string message) => Messages.Add(message);
    }
}
