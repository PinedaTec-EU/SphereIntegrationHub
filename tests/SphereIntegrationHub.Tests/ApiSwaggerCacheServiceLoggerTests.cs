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

        var catalog = new ApiCatalogVersion
        {
            Version = "v1",
            Definitions = new List<ApiDefinition>
            {
                new ApiDefinition { Name = "accounts", SwaggerUrl = new Uri(sourcePath).AbsoluteUri, BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["test"] = "https://example.test" } }
            }
        };

        var cacheRoot = Path.Combine(tempRoot, "cache");

        await service.CacheSwaggerAsync(catalog, "test", cacheRoot, refresh: true, verbose: true, CancellationToken.None);

        Assert.Contains(logger.Messages, message => message.Contains("Swagger cached", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CacheSwaggerAsync_WithRelativeSwaggerPath_DownloadsFromResolvedBaseUrl()
    {
        var logger = new TestLogger();
        var handler = new FakeHttpMessageHandler("""{"openapi":"3.0.1","paths":{}}""");
        var httpClient = new HttpClient(handler);
        var service = new ApiSwaggerCacheService(httpClient, logger);
        var tempRoot = Path.Combine(Path.GetTempPath(), $"aos-swagger-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        var catalog = new ApiCatalogVersion
        {
            Version = "v1",
            Definitions = new List<ApiDefinition>
            {
                new ApiDefinition
                {
                    Name = "licensing",
                    SwaggerUrl = "/swagger/v1/swagger.json",
                    BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["local"] = "http://localhost:5004"
                    }
                }
            }
        };

        var cacheRoot = Path.Combine(tempRoot, "cache");

        await service.CacheSwaggerAsync(catalog, "local", cacheRoot, refresh: true, verbose: true, CancellationToken.None);

        Assert.Equal(new Uri("http://localhost:5004/swagger/v1/swagger.json"), handler.LastRequestUri);
        Assert.True(File.Exists(Path.Combine(cacheRoot, "licensing.json")));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("Swagger source file was not found", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CacheSwaggerAsync_SkipsLlmDefinitions()
    {
        var logger = new TestLogger();
        var handler = new FakeHttpMessageHandler("""{"openapi":"3.0.1","paths":{}}""");
        var httpClient = new HttpClient(handler);
        var service = new ApiSwaggerCacheService(httpClient, logger);
        var tempRoot = Path.Combine(Path.GetTempPath(), $"aos-swagger-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        var catalog = new ApiCatalogVersion
        {
            Version = "v1",
            Definitions = new List<ApiDefinition>
            {
                new ApiDefinition
                {
                    Name = "openai-main",
                    ContractType = ApiContractTypes.Llm,
                    BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["local"] = "https://api.openai.com/v1"
                    },
                    ApiKeySecret = "{{input.openaiApiKey}}"
                }
            }
        };

        var cacheRoot = Path.Combine(tempRoot, "cache");

        var operations = await service.CacheSwaggerAsync(catalog, "local", cacheRoot, refresh: true, verbose: true, CancellationToken.None);

        Assert.Empty(operations);
        Assert.Null(handler.LastRequestUri);
        Assert.False(File.Exists(Path.Combine(cacheRoot, "openai-main.json")));
        Assert.Contains(logger.Messages, message => message.Contains("Skipping swagger cache for LLM definition", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class TestLogger : IExecutionLogger
    {
        public List<string> Messages { get; } = new();

        public void Info(string message) => Messages.Add(message);

        public void Error(string message) => Messages.Add(message);
    }

    private sealed class FakeHttpMessageHandler(string payload) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(payload)
            });
        }
    }
}
