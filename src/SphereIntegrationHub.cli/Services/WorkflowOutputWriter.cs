using System.Text.Json;

using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services.Interfaces;

namespace SphereIntegrationHub.Services;

public sealed class WorkflowOutputWriter : IWorkflowOutputWriter
{
    public async Task<string?> WriteOutputAsync(
        WorkflowDefinition definition,
        WorkflowDocument document,
        IReadOnlyDictionary<string, string> outputs,
        CancellationToken cancellationToken)
    {
        using var activity = Telemetry.ActivitySource.StartActivity(TelemetryConstants.ActivityWorkflowOutputWrite);
        activity?.SetTag(TelemetryConstants.TagWorkflowName, definition.Name);
        if (!definition.Output)
        {
            return null;
        }

        var baseDirectory = Path.GetDirectoryName(document.FilePath) ?? string.Empty;
        var safeName = definition.Name.Replace(' ', '-');
        var outputDirectory = Path.Combine(baseDirectory, "output");
        Directory.CreateDirectory(outputDirectory);
        var suffix = Ulid.NewUlid().ToString();
        var fileName = $"{safeName}.{definition.Id}.{suffix}.workflow.output";
        var outputFilePath = Path.Combine(outputDirectory, fileName);

        var outputPayload = BuildOutputPayload(outputs, definition.EndStage?.OutputJson ?? true);
        var json = JsonSerializer.Serialize(outputPayload, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(outputFilePath, json, cancellationToken);
        return outputFilePath;
    }

    private static IReadOnlyDictionary<string, object?> BuildOutputPayload(
        IReadOnlyDictionary<string, string> outputs,
        bool outputJson)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in outputs)
        {
            if (outputJson && TryParseJsonValue(pair.Value, out var parsed))
            {
                payload[pair.Key] = parsed;
            }
            else
            {
                payload[pair.Key] = pair.Value;
            }
        }

        return payload;
    }

    private static bool TryParseJsonValue(string value, out object? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (!(trimmed.StartsWith('{') || trimmed.StartsWith('[')))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            parsed = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
