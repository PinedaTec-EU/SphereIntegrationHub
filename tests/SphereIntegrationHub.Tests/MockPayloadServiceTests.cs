using SphereIntegrationHub.Services;

namespace SphereIntegrationHub.Tests;

public sealed class MockPayloadServiceTests
{
    [Fact]
    public void LoadRawPayload_ReturnsLiteral()
    {
        var service = new MockPayloadService();
        var result = service.LoadRawPayload("{\"id\":\"1\"}", "/tmp/workflow.workflow");

        Assert.Equal("{\"id\":\"1\"}", result);
    }

    [Fact]
    public void LoadRawPayloadFromFile_ReadsRelativePath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var payloadPath = Path.Combine(tempDir, "payload.json");
        File.WriteAllText(payloadPath, "{\"id\":\"abc\"}");

        try
        {
            var service = new MockPayloadService();
            var workflowPath = Path.Combine(tempDir, "workflow.workflow");

            var result = service.LoadRawPayloadFromFile("payload.json", workflowPath);

            Assert.Equal("{\"id\":\"abc\"}", result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void TryParseJson_RejectsInvalidJson()
    {
        var ok = MockPayloadService.TryParseJson("{\"id\":1}", out var error);
        Assert.True(ok);
        Assert.Null(error);

        var bad = MockPayloadService.TryParseJson("{\"id\":", out error);
        Assert.False(bad);
        Assert.NotNull(error);
    }
}
