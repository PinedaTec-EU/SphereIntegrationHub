using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;

namespace SphereIntegrationHub.Tests;

public sealed class HttpRequestBuilderTests
{
    [Fact]
    public async Task Build_ResolvesBodyFileFromTemplatePath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"sih-http-builder-{Guid.NewGuid():N}");
        var tenantDir = Path.Combine(tempDir, "tenant-a");
        Directory.CreateDirectory(tenantDir);
        var workflowPath = Path.Combine(tempDir, "sample.workflow");
        var bodyFilePath = Path.Combine(tenantDir, "body.json");
        File.WriteAllText(bodyFilePath, """{"tenant":"{{input.tenant}}"}""");

        try
        {
            var builder = new HttpRequestBuilder(new TemplateResolver());
            var request = builder.Build(
                new WorkflowStageDefinition
                {
                    Name = "create",
                    Kind = WorkflowStageKind.Endpoint,
                    Endpoint = "/api/accounts",
                    HttpVerb = "POST",
                    BodyFile = "./{{input.tenant}}/body.json"
                },
                "http://example.test",
                new TemplateContext(
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["tenant"] = "tenant-a"
                    },
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    WorkflowPath: workflowPath));

            Assert.NotNull(request.Content);
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Equal("""{"tenant":"tenant-a"}""", body);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
