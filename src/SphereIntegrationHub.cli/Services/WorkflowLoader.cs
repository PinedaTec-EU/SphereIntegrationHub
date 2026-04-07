using System.Collections.Concurrent;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.Services;

public sealed class WorkflowLoader
{
    private readonly ConcurrentDictionary<string, (DateTime LastWrite, WorkflowDocument Document)> _documentCache = new();

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

        var fullPath = Path.GetFullPath(workflowPath);

        // Cache only when there are no env overrides — avoids key-space explosion.
        // Workflows referencing an environmentFile are excluded from the cache write below
        // so that changes to the env file always produce a fresh load.
        var isCacheCandidate = envFileOverride is null && (parentEnvironment is null || parentEnvironment.Count == 0);
        if (isCacheCandidate)
        {
            var lastWrite = File.GetLastWriteTimeUtc(fullPath);
            if (_documentCache.TryGetValue(fullPath, out var cached) && cached.LastWrite == lastWrite)
            {
                return cached.Document;
            }
        }

        try
        {
            var yaml = File.ReadAllText(fullPath);
            var definition = _deserializer.Deserialize<WorkflowDefinition>(yaml);
            if (definition is null)
            {
                throw new InvalidOperationException("Workflow file is empty or invalid.");
            }

            var environmentVariables = ResolveEnvironmentVariables(definition, fullPath, parentEnvironment, envFileOverride);
            var document = new WorkflowDocument(definition, fullPath, environmentVariables);

            // Only cache when the workflow itself carries no env file reference,
            // ensuring a change to an external env file is never missed.
            if (isCacheCandidate && definition.References?.EnvironmentFile is null)
            {
                _documentCache[fullPath] = (File.GetLastWriteTimeUtc(fullPath), document);
            }

            return document;
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
