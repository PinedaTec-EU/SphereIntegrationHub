using System.Text.Json;

using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;

namespace SphereIntegrationHub.Tests;

public sealed class WorkflowExecutorSecretMaskingTests
{
    [Fact]
    public async Task ExecuteAsync_MasksSecretsInReturnedOutputAndOutputFile()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"sih-secret-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var definition = new WorkflowDefinition
            {
                Version = "1.0",
                Id = "secret-mask",
                Name = "secret mask",
                Output = true,
                Input = new List<WorkflowInputDefinition>
                {
                    new() { Name = "secretKey", Type = RandomValueType.Text, Required = true, Secret = true }
                },
                EndStage = new WorkflowEndStage
                {
                    Output = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["secretKey"] = "{{input.secretKey}}",
                        ["combined"] = "visible=value; secret={{input.secretKey}}",
                        ["payload"] = "{\"combined\":\"visible=value; secret={{input.secretKey}}\"}"
                    }
                }
            };

            var document = new WorkflowDocument(definition, Path.Combine(tempRoot, "secret-mask.workflow"), new Dictionary<string, string>());
            var catalogVersion = new ApiCatalogVersion
            {
                Version = "test",
                Definitions = new List<ApiDefinition>()
            };

            using var httpClient = new HttpClient();
            var executor = new WorkflowExecutor(
                httpClient,
                new DynamicValueService(),
                reportOptions: new WorkflowExecutionReportOptions(false, ExecutionReportFormat.None, ExecutionHttpCaptureMode.None, true, false));

            var result = await executor.ExecuteAsync(
                document,
                catalogVersion,
                "test",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["secretKey"] = "super-secret-token"
                },
                varsOverrideActive: false,
                mocked: false,
                verbose: false,
                debug: false,
                cancellationToken: CancellationToken.None);

            Assert.Equal("*****", result.Output["secretKey"]);
            Assert.Equal("visible=value; secret=*****", result.Output["combined"]);

            var json = await File.ReadAllTextAsync(result.OutputFilePath!);
            using var parsed = JsonDocument.Parse(json);
            Assert.Equal("*****", parsed.RootElement.GetProperty("secretKey").GetString());
            Assert.Equal("visible=value; secret=*****", parsed.RootElement.GetProperty("combined").GetString());
            Assert.Equal("visible=value; secret=*****", parsed.RootElement.GetProperty("payload").GetProperty("combined").GetString());
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }
}
