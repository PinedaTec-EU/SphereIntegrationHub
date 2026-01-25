using System.Text.Json;

using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;

namespace SphereIntegrationHub.Tests;

public sealed class WorkflowOutputWriterTests
{
    [Fact]
    public async Task WriteOutputAsync_WritesJsonPayload()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"aos-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var workflowPath = Path.Combine(tempRoot, "workflow.yaml");

        var definition = new WorkflowDefinition
        {
            Id = "test",
            Name = "test workflow",
            Output = true,
            EndStage = new WorkflowEndStage
            {
                OutputJson = true
            }
        };

        var outputs = new Dictionary<string, string>
        {
            ["name"] = "value",
            ["payload"] = "{\"id\":1}"
        };

        var document = new WorkflowDocument(definition, workflowPath, new Dictionary<string, string>());
        var writer = new WorkflowOutputWriter();

        var outputFilePath = await writer.WriteOutputAsync(definition, document, outputs, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(outputFilePath));
        Assert.True(File.Exists(outputFilePath));

        var json = await File.ReadAllTextAsync(outputFilePath!);
        using var parsed = JsonDocument.Parse(json);
        Assert.Equal("value", parsed.RootElement.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Object, parsed.RootElement.GetProperty("payload").ValueKind);
        Assert.Equal(1, parsed.RootElement.GetProperty("payload").GetProperty("id").GetInt32());
    }
}
