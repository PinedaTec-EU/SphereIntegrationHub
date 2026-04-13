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
        const string executionId = "exec-1";

        var outputFilePath = await writer.WriteOutputAsync(definition, document, executionId, outputs, secretKeys: null, secretValues: null, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(outputFilePath));
        Assert.True(File.Exists(outputFilePath));
        Assert.EndsWith($"{Path.DirectorySeparatorChar}test-workflow.{executionId}.workflow.output", outputFilePath, StringComparison.Ordinal);

        var json = await File.ReadAllTextAsync(outputFilePath!);
        using var parsed = JsonDocument.Parse(json);
        Assert.Equal("value", parsed.RootElement.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Object, parsed.RootElement.GetProperty("payload").ValueKind);
        Assert.Equal(1, parsed.RootElement.GetProperty("payload").GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task WriteOutputAsync_RedactsSecretKeysAndEmbeddedSecretValues()
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

        const string secretValue = "super-secret-token";
        var outputs = new Dictionary<string, string>
        {
            ["secretKey"] = secretValue,
            ["combined"] = $"visible=value; secret={secretValue}",
            ["payload"] = $$"""{"combined":"visible=value; secret={{secretValue}}"}"""
        };

        var document = new WorkflowDocument(definition, workflowPath, new Dictionary<string, string>());
        var writer = new WorkflowOutputWriter();
        const string executionId = "exec-1";

        var outputFilePath = await writer.WriteOutputAsync(
            definition,
            document,
            executionId,
            outputs,
            secretKeys: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "secretKey" },
            secretValues: new HashSet<string>(StringComparer.Ordinal) { secretValue },
            CancellationToken.None);

        var json = await File.ReadAllTextAsync(outputFilePath!);
        using var parsed = JsonDocument.Parse(json);

        Assert.Equal("*****", parsed.RootElement.GetProperty("secretKey").GetString());
        Assert.Equal("visible=value; secret=*****", parsed.RootElement.GetProperty("combined").GetString());
        Assert.Equal("visible=value; secret=*****", parsed.RootElement.GetProperty("payload").GetProperty("combined").GetString());
    }

    [Fact]
    public async Task WriteOutputAsync_DoesNotWriteFileWhenWorkflowOutputIsDisabled()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"aos-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var workflowPath = Path.Combine(tempRoot, "workflow.yaml");

        var definition = new WorkflowDefinition
        {
            Id = "test",
            Name = "test workflow",
            Output = false,
            EndStage = new WorkflowEndStage
            {
                OutputJson = true
            }
        };

        var outputs = new Dictionary<string, string>
        {
            ["name"] = "value"
        };

        var document = new WorkflowDocument(definition, workflowPath, new Dictionary<string, string>());
        var writer = new WorkflowOutputWriter();

        var outputFilePath = await writer.WriteOutputAsync(definition, document, "exec-1", outputs, secretKeys: null, secretValues: null, CancellationToken.None);

        Assert.Null(outputFilePath);
        Assert.False(Directory.Exists(Path.Combine(tempRoot, "output")));
    }
}
