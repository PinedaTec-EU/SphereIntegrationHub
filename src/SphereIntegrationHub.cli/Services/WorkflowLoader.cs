using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.Services;

public sealed class WorkflowLoader
{
    private readonly IDeserializer _deserializer;
    private readonly EnvironmentFileLoader _envLoader;

    public WorkflowLoader()
    {
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        _envLoader = new EnvironmentFileLoader();
    }

    public WorkflowDocument Load(
        string workflowPath,
        IReadOnlyDictionary<string, string>? parentEnvironment = null,
        string? envFileOverride = null)
    {
        using var activity = Telemetry.ActivitySource.StartActivity(TelemetryConstants.ActivityWorkflowLoad);
        activity?.SetTag(TelemetryConstants.TagWorkflowPath, workflowPath);
        if (string.IsNullOrWhiteSpace(workflowPath))
        {
            throw new ArgumentException("Workflow path is required.", nameof(workflowPath));
        }

        if (!File.Exists(workflowPath))
        {
            throw new FileNotFoundException("Workflow file was not found.", workflowPath);
        }

        try
        {
            var yaml = File.ReadAllText(workflowPath);
            var definition = _deserializer.Deserialize<WorkflowDefinition>(yaml);
            if (definition is null)
            {
                throw new InvalidOperationException("Workflow file is empty or invalid.");
            }

            var environmentVariables = ResolveEnvironmentVariables(definition, workflowPath, parentEnvironment, envFileOverride);
            return new WorkflowDocument(definition, Path.GetFullPath(workflowPath), environmentVariables);
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            var location = ex.Start.Line > 0
                ? $" (line {ex.Start.Line}, column {ex.Start.Column})"
                : string.Empty;
            var detail = ex.InnerException?.Message;
            var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" Details: {detail}";
            throw new InvalidOperationException($"Failed to parse workflow YAML{location}: {ex.Message}{suffix}", ex);
        }
    }

    private IReadOnlyDictionary<string, string> ResolveEnvironmentVariables(
        WorkflowDefinition definition,
        string workflowPath,
        IReadOnlyDictionary<string, string>? parentEnvironment,
        string? envFileOverride)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var envFile = envFileOverride ?? definition.References?.EnvironmentFile;
        if (!string.IsNullOrWhiteSpace(envFile))
        {
            var baseDirectory = envFileOverride is null
                ? Path.GetDirectoryName(workflowPath) ?? string.Empty
                : Directory.GetCurrentDirectory();
            var resolvedPath = Path.IsPathRooted(envFile)
                ? envFile
                : Path.GetFullPath(Path.Combine(baseDirectory, envFile));

            foreach (var pair in _envLoader.Load(resolvedPath))
            {
                variables[pair.Key] = pair.Value;
            }
        }

        if (parentEnvironment is not null)
        {
            foreach (var pair in parentEnvironment)
            {
                variables[pair.Key] = pair.Value;
            }
        }

        return variables;
    }
}
