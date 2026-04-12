using SphereIntegrationHub.Services;

namespace SphereIntegrationHub.Tests;

public sealed class WorkflowDataFileServiceTests
{
    [Fact]
    public void LoadText_ResolvesTemplatePath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"sih-data-file-{Guid.NewGuid():N}");
        var tenantDir = Path.Combine(tempDir, "tenant-a");
        Directory.CreateDirectory(tenantDir);
        var workflowPath = Path.Combine(tempDir, "sample.workflow");
        var dataFilePath = Path.Combine(tenantDir, "items.json");
        File.WriteAllText(dataFilePath, "[1,2,3]");

        try
        {
            var service = new WorkflowDataFileService();
            var content = service.LoadText(
                "./{{input.tenant}}/items.json",
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

            Assert.Equal("[1,2,3]", content);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
